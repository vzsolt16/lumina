using System.Text.Json;
using Lumina.Data;
using Lumina.Models;
using Lumina.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumina.Tests;

public sealed class DocumentIndexServiceTests
{
    [Fact]
    public async Task ReplacementActivatesNewGenerationThenDeletesOldGeneration()
    {
        var options = new DbContextOptionsBuilder<LuminaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var id = Guid.NewGuid();
        var largeText = string.Join(' ', Enumerable.Repeat("meaningful text.", 2500));
        var contentJson = JsonSerializer.Serialize(new
        {
            type = "doc",
            content = new object[]
            {
                new
                {
                    type = "paragraph",
                    attrs = new { id = "paragraph-1" },
                    content = new object[] { new { type = "text", text = largeText } }
                }
            }
        });
        await using (var seed = new LuminaDbContext(options))
        {
            seed.Documents.Add(new Document
            {
                Id = id, UserId = Guid.NewGuid(), FileName = "large.txt", ContentVersion = 3,
                Content = string.Join(' ', Enumerable.Repeat("meaningful text", 2500)),
                ContentJson = contentJson,
                IndexingStatus = "Indexed", IndexedContentVersion = 2, ActiveIndexGeneration = "previous"
            });
            await seed.SaveChangesAsync();
        }

        await using var db = new LuminaDbContext(options);
        var vectors = new RecordingVectorStore();
        var service = new DocumentIndexService(db, new DocumentChunker(new ConfigurationBuilder().Build()), new FakeEmbeddings(), vectors,
            NullLogger<DocumentIndexService>.Instance, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rag:ContextTokenBudget"] = "6000" }).Build());
        await service.IndexAsync(id, default);

        var stored = await db.Documents.SingleAsync(d => d.Id == id);
        Assert.Equal("Indexed", stored.IndexingStatus);
        Assert.Equal(3, stored.IndexedContentVersion);
        Assert.NotEqual("previous", stored.ActiveIndexGeneration);
        Assert.Contains((id, "previous"), vectors.DeletedGenerations);
        Assert.NotEmpty(vectors.Upserted);
        Assert.All(vectors.Upserted, point => Assert.Equal(stored.ActiveIndexGeneration, point.Generation));
    }

    [Fact]
    public async Task IndexesCanonicalMarkdownWithHeadingAndFormatMetadata()
    {
        var options = new DbContextOptionsBuilder<LuminaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var id = Guid.NewGuid();
        var section = string.Join(' ', Enumerable.Repeat("meaningful retrieval passage", 2500));
        await using (var seed = new LuminaDbContext(options))
        {
            seed.Documents.Add(new Document
            {
                Id = id, UserId = Guid.NewGuid(), FileName = "notes.md", ContentVersion = 1,
                Content = "# Biology\n\n## Roots\n" + section, ContentJson = ""
            });
            await seed.SaveChangesAsync();
        }
        await using var db = new LuminaDbContext(options);
        var vectors = new RecordingVectorStore();
        var service = new DocumentIndexService(db, new DocumentChunker(new ConfigurationBuilder().Build()), new FakeEmbeddings(), vectors,
            NullLogger<DocumentIndexService>.Instance, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rag:FullDocumentMaxTokens"] = "100" }).Build());

        await service.IndexAsync(id, default);

        Assert.NotEmpty(vectors.Upserted);
        Assert.All(vectors.Upserted, point => Assert.Equal(".md", point.Chunk.Format));
        Assert.Contains(vectors.Upserted, point => point.Chunk.HeadingPath?.SequenceEqual(["Biology", "Roots"]) == true);
        Assert.Equal("format-aware-v3", (await db.Documents.SingleAsync(d => d.Id == id)).ChunkingVersion);
    }

    private sealed class FakeEmbeddings : IEmbeddingService
    {
        public string Model => "embedding-test"; public int? Dimensions => 3;
        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, string title, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 1, 0, 0 }).ToArray());
        public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken) => Task.FromResult(new float[] { 1, 0, 0 });
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public List<VectorRecord> Upserted { get; } = [];
        public List<(Guid, string)> DeletedGenerations { get; } = [];
        public Task EnsureCollectionAsync(int dimensions, CancellationToken ct) { Assert.Equal(3, dimensions); return Task.CompletedTask; }
        public Task UpsertAsync(IReadOnlyList<VectorRecord> points, CancellationToken ct) { Upserted.AddRange(points); return Task.CompletedTask; }
        public Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] vector, Guid userId, Guid? documentId, string generation, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<VectorMatch>>([]);
        public Task DeleteDocumentAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteGenerationAsync(Guid documentId, string generation, CancellationToken ct) { DeletedGenerations.Add((documentId, generation)); return Task.CompletedTask; }
    }
}
