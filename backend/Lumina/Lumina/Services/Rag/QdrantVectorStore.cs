using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumina.Services.Rag;

public sealed record VectorRecord(Guid Id, float[] Vector, Guid UserId, Guid DocumentId, int DocumentVersion,
    string Generation, DocumentChunk Chunk, string EmbeddingModel);
public sealed record VectorMatch(Guid DocumentId, int DocumentVersion, string Generation, DocumentChunk Chunk, float Score);

public interface IVectorStore
{
    Task EnsureCollectionAsync(int dimensions, CancellationToken ct);
    Task UpsertAsync(IReadOnlyList<VectorRecord> points, CancellationToken ct);
    Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] vector, Guid userId, Guid? documentId, string generation, int limit, CancellationToken ct);
    Task DeleteDocumentAsync(Guid documentId, CancellationToken ct);
    Task DeleteGenerationAsync(Guid documentId, string generation, CancellationToken ct);
}

/// <summary>Qdrant documented REST API adapter. The shared collection stores all tenants; every retrieval filters owner and generation.</summary>
public sealed class QdrantVectorStore(HttpClient http, IConfiguration config) : IVectorStore
{
    private readonly string _collection = config["Qdrant:Collection"] ?? "lumina_document_chunks";

    public async Task EnsureCollectionAsync(int dimensions, CancellationToken ct)
    {
        using var get = await http.GetAsync($"collections/{Uri.EscapeDataString(_collection)}", ct);
        if (get.IsSuccessStatusCode)
        {
            using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync(ct));
            var actual = json.RootElement.GetProperty("result").GetProperty("config").GetProperty("params").GetProperty("vectors").GetProperty("size").GetInt32();
            if (actual != dimensions) throw new InvalidOperationException($"Qdrant collection dimension is {actual}; embedding model returned {dimensions}.");
            return;
        }
        if ((int)get.StatusCode != 404) { get.EnsureSuccessStatusCode(); }
        using var create = await http.PutAsJsonAsync($"collections/{Uri.EscapeDataString(_collection)}", new { vectors = new { size = dimensions, distance = "Cosine" } }, ct);
        if (create.IsSuccessStatusCode) return;
        // Another document may create the single shared collection between our
        // 404 and PUT. Confirm its schema instead of failing that indexing run.
        if ((int)create.StatusCode == 409)
        {
            using var retry = await http.GetAsync($"collections/{Uri.EscapeDataString(_collection)}", ct);
            retry.EnsureSuccessStatusCode();
            using var schema = JsonDocument.Parse(await retry.Content.ReadAsStringAsync(ct));
            var actual = schema.RootElement.GetProperty("result").GetProperty("config").GetProperty("params").GetProperty("vectors").GetProperty("size").GetInt32();
            if (actual == dimensions) return;
            throw new InvalidOperationException($"Qdrant collection dimension is {actual}; embedding model returned {dimensions}.");
        }
        create.EnsureSuccessStatusCode();
    }

    public async Task UpsertAsync(IReadOnlyList<VectorRecord> points, CancellationToken ct)
    {
        if (points.Count == 0) return;
        using var response = await http.PutAsJsonAsync($"collections/{_collection}/points?wait=true", new { points = points.Select(p => new { id = p.Id, vector = p.Vector, payload = new { userId = p.UserId, documentId = p.DocumentId, documentVersion = p.DocumentVersion, generation = p.Generation, chunkIndex = p.Chunk.Index, text = p.Chunk.Text, heading = p.Chunk.Heading, nodeIds = p.Chunk.NodeIds, embeddingModel = p.EmbeddingModel } }) }, ct);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (result.RootElement.TryGetProperty("status", out var status) && status.GetString() != "ok") throw new InvalidOperationException("Qdrant did not confirm vector insertion.");
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] vector, Guid userId, Guid? documentId, string generation, int limit, CancellationToken ct)
    {
        var must = new List<object> { Match("userId", userId.ToString()), Match("generation", generation) };
        if (documentId is not null) must.Add(Match("documentId", documentId.Value.ToString()));
        using var response = await http.PostAsJsonAsync($"collections/{_collection}/points/search", new { vector, limit, with_payload = true, filter = new { must } }, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var result = json.RootElement.GetProperty("result");
        return result.EnumerateArray().Select(p =>
        {
            var payload = p.GetProperty("payload");
            var ids = payload.TryGetProperty("nodeIds", out var ni) ? ni.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
            return new VectorMatch(Guid.Parse(payload.GetProperty("documentId").GetString()!), payload.GetProperty("documentVersion").GetInt32(), payload.GetProperty("generation").GetString()!, new DocumentChunk(payload.GetProperty("chunkIndex").GetInt32(), payload.GetProperty("text").GetString() ?? "", payload.TryGetProperty("heading", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null, ids), p.GetProperty("score").GetSingle());
        }).ToArray();
    }

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken ct) => DeleteAsync(new[] { Match("documentId", documentId.ToString()) }, ct);
    public Task DeleteGenerationAsync(Guid documentId, string generation, CancellationToken ct) => DeleteAsync(new[] { Match("documentId", documentId.ToString()), Match("generation", generation) }, ct);
    private async Task DeleteAsync(object[] must, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync($"collections/{_collection}/points/delete?wait=true", new { filter = new { must } }, ct);
        if ((int)response.StatusCode == 404) return;
        response.EnsureSuccessStatusCode();
    }
    private static object Match(string key, string value) => new { key, match = new { value } };
}
