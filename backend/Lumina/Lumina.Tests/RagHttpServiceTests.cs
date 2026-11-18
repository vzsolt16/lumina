using System.Net;
using System.Text;
using System.Text.Json;
using Lumina.Services.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumina.Tests;

public sealed class RagHttpServiceTests
{
    [Fact]
    public async Task OllamaEmbeddingsUseRetrievalPrefixesAndValidateDimensions()
    {
        var handler = new StubHandler((request, body) =>
        {
            Assert.Equal("api/embed", request.RequestUri!.PathAndQuery.TrimStart('/'));
            using var payload = JsonDocument.Parse(body);
            var inputs = payload.RootElement.GetProperty("input").EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (inputs[0].StartsWith("task:", StringComparison.Ordinal))
                Assert.Equal("task: search result | query: question", inputs[0]);
            else
            {
                Assert.Contains(inputs, x => x == "title: notes.txt | text: paragraph");
                Assert.Contains(inputs, x => x == "title: notes.txt | text: second");
            }
            var vectors = string.Join(',', inputs.Select(_ => "[0.1,0.2,0.3]"));
            return Json($"{{\"embeddings\":[{vectors}]}}");
        });
        var service = new OllamaEmbeddingService(new HttpClient(handler) { BaseAddress = new Uri("http://ollama/") },
            Config(("Ollama:EmbeddingModel", "embeddinggemma-2:270m")), NullLogger<OllamaEmbeddingService>.Instance);
        var docs = await service.EmbedDocumentsAsync(["paragraph", "second"], "notes.txt", default);
        var query = await service.EmbedQueryAsync("question", default);
        Assert.Equal(2, docs.Count);
        Assert.Equal(3, query.Length);
        Assert.Equal(3, service.Dimensions);
    }

    [Fact]
    public async Task QdrantCreatesDimensionCheckedCollectionAndUsesTenantGenerationFilters()
    {
        var searchPayload = """{"result":[{"id":"e9204663-e8a7-4960-b31d-d4b999c026a8","score":0.8,"payload":{"documentId":"d9204663-e8a7-4960-b31d-d4b999c026a8","documentVersion":4,"generation":"active","chunkIndex":2,"text":"passage","heading":"Topic","nodeIds":["node-2"]}}]}""";
        var handler = new StubHandler((request, body) => request.Method switch
        {
            var method when method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/collections/lumina_document_chunks") => new(HttpStatusCode.NotFound),
            var method when method == HttpMethod.Put && request.RequestUri!.AbsolutePath.EndsWith("/collections/lumina_document_chunks") => Json("""{"result":true,"status":"ok"}"""),
            var method when method == HttpMethod.Put && request.RequestUri!.AbsolutePath.Contains("/points?wait=true") => Json("""{"result":{"status":"completed"},"status":"ok"}"""),
            var method when method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/points/search") => Json(searchPayload),
            _ => Json("""{"result":{"status":"completed"},"status":"ok"}""")
        });
        var store = new QdrantVectorStore(new HttpClient(handler) { BaseAddress = new Uri("http://qdrant/") }, Config());
        await store.EnsureCollectionAsync(3, default);
        var chunk = new DocumentChunk(2, "passage", "Topic", ["node-2"]);
        await store.UpsertAsync([new VectorRecord(Guid.NewGuid(), [0, 0, 1], Guid.NewGuid(), Guid.NewGuid(), 4, "active", chunk, "model")], default);
        var userId = Guid.NewGuid(); var documentId = Guid.Parse("d9204663-e8a7-4960-b31d-d4b999c026a8");
        var matches = await store.SearchAsync([0, 0, 1], userId, documentId, "active", 5, default);
        Assert.Equal("Topic", matches.Single().Chunk.Heading);
        Assert.Equal("node-2", matches.Single().Chunk.NodeIds.Single());
        Assert.Contains($"\"value\":\"{userId:D}\"", handler.LastSearchBody);
        Assert.Contains("\"key\":\"userId\"", handler.LastSearchBody);
        Assert.Contains("\"key\":\"generation\"", handler.LastSearchBody);
        Assert.Contains(documentId.ToString(), handler.LastSearchBody);
        await store.DeleteDocumentAsync(documentId, default);
        Assert.Contains("points/delete", string.Join('\n', handler.Paths));
    }

    private static IConfiguration Config(params (string Key, string Value)[] entries) => new ConfigurationBuilder()
        .AddInMemoryCollection(entries.ToDictionary(x => x.Key, x => (string?)x.Value)).Build();
    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string LastSearchBody { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Paths.Add(request.RequestUri!.PathAndQuery);
            if (request.RequestUri.AbsolutePath.EndsWith("/points/search")) LastSearchBody = body;
            return responder(request, body);
        }
    }
}
