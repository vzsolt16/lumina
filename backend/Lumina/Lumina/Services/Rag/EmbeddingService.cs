using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Lumina.Services.Rag;

public interface IEmbeddingService
{
    string Model { get; }
    int? Dimensions { get; }
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, string title, CancellationToken cancellationToken);
    Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken);
}

public sealed class OllamaEmbeddingService(HttpClient http, IConfiguration configuration, ILogger<OllamaEmbeddingService> logger) : IEmbeddingService
{
    public string Model { get; } = configuration["Ollama:EmbeddingModel"] ?? "embeddinggemma-2:270m";
    public int? Dimensions { get; private set; }

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, string title, CancellationToken cancellationToken) =>
        EmbedAsync(texts.Select(t => $"title: {title} | text: {t}").ToArray(), cancellationToken);

    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        var result = await EmbedAsync([$"task: search result | query: {query}"], cancellationToken);
        return result[0];
    }

    private async Task<IReadOnlyList<float[]>> EmbedAsync(string[] input, CancellationToken ct)
    {
        if (input.Length == 0) return [];
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            logger.LogDebug("Embedding generation started for {TextCount} texts with model {ModelId}", input.Length, Model);
            using var response = await http.PostAsJsonAsync("api/embed", new { model = Model, input }, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Ollama returned an empty embedding response.");
            if (body.Embeddings is null || body.Embeddings.Length != input.Length || body.Embeddings.Any(v => v.Length == 0))
                throw new InvalidOperationException("Ollama returned an unexpected number of embeddings or an empty vector.");
            var dimensions = body.Embeddings[0].Length;
            if (body.Embeddings.Any(v => v.Length != dimensions)) throw new InvalidOperationException("Ollama returned vectors with inconsistent dimensions.");
            if (Dimensions is int previous && previous != dimensions) throw new InvalidOperationException($"Embedding dimension changed from {previous} to {dimensions}.");
            Dimensions = dimensions;
            logger.LogInformation("Ollama embedded {TextCount} texts with model {ModelId} in {DurationMs} ms with {Dimensions} dimensions", input.Length, Model, started.ElapsedMilliseconds, dimensions);
            return body.Embeddings;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Ollama embedding timed out for {TextCount} texts", input.Length);
            throw new TimeoutException("Ollama embedding request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Ollama embedding request failed for {TextCount} texts", input.Length);
            throw new InvalidOperationException("Ollama embedding request failed.", ex);
        }
    }

    private sealed class EmbedResponse { [JsonPropertyName("embeddings")] public float[][]? Embeddings { get; set; } }
}
