using Lumina.Data;
using Lumina.Models;
using Lumina.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumina.Tests;

public sealed class DocumentRelevanceTests
{
    [Fact]
    public async Task IrrelevantLargeDocumentReturnsNone()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var document = NewDocument(userId, new string('x', 20000), 2);
        document.IndexingStatus = "Indexed"; document.IndexedContentVersion = 2;
        document.ActiveIndexGeneration = "active"; document.ChunkingVersion = DocumentChunker.Version;
        db.Documents.Add(document); await db.SaveChangesAsync();
        
        // Return a very low score
        var vectors = new FakeVectors { Result = [new VectorMatch(document.Id, 2, "active", new DocumentChunk(0, "unrelated", "Topic", ["node-1"]), 0.1f)] };
        using var provider = CreateProvider(db);
        
        // Threshold is 0.3, score is 0.1, so it should be irrelevant
        var selector = CreateSelector(provider.GetRequiredService<IServiceScopeFactory>(), new FakeEmbeddings(), vectors, 1500);
        var result = await selector.SelectAsync(document.Id, userId, "How to build a spaceship?", [], default);
        
        Assert.Equal("none", result.Mode);
        Assert.Equal("Irrelevant document.", result.Limitation);
    }

    [Fact]
    public async Task RelevantLargeDocumentProceedsToRetrieval()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var document = NewDocument(userId, new string('x', 20000), 2);
        document.IndexingStatus = "Indexed"; document.IndexedContentVersion = 2;
        document.ActiveIndexGeneration = "active"; document.ChunkingVersion = DocumentChunker.Version;
        db.Documents.Add(document); await db.SaveChangesAsync();
        
        // Return a high score
        var vectors = new FakeVectors { Result = [new VectorMatch(document.Id, 2, "active", new DocumentChunk(0, "highly relevant passage", "Topic", ["node-1"]), 0.9f)] };
        using var provider = CreateProvider(db);
        
        // Threshold is 0.3, score is 0.9, so it should be relevant
        var selector = CreateSelector(provider.GetRequiredService<IServiceScopeFactory>(), new FakeEmbeddings(), vectors, 1500);
        var result = await selector.SelectAsync(document.Id, userId, "How to build a spaceship?", [], default);
        
        // Should NOT be "none" because it's relevant
        Assert.NotEqual("none", result.Mode);
    }

    private static LuminaDbContext CreateDb() => new(new DbContextOptionsBuilder<LuminaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Document NewDocument(Guid userId, string content, int version) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, FileName = "notes.txt", Content = content,
        ContentJson = "{\"type\":\"doc\",\"content\":[]}", ContentVersion = version
    };
    private static ServiceProvider CreateProvider(LuminaDbContext db) => new ServiceCollection().AddSingleton(db).BuildServiceProvider();
    
    private static DocumentContextSelector CreateSelector(IServiceScopeFactory scopes, FakeEmbeddings embeddings, FakeVectors vectors, int budget) =>
        new(scopes, embeddings, vectors, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { 
            ["Rag:ContextTokenBudget"] = budget.ToString(),
            ["Rag:RelevanceThreshold"] = "0.3"
        }).Build(), NullLogger<DocumentContextSelector>.Instance);

    private sealed class FakeEmbeddings : IEmbeddingService
    {
        public string Model => "test"; public int? Dimensions => 3; public int QueryCalls { get; private set; }
        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, string title, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[3]).ToArray());
        public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken) { QueryCalls++; return Task.FromResult(new float[3]); }
    }
    
    private sealed class FakeVectors : IVectorStore
    {
        public IReadOnlyList<VectorMatch> Result { get; init; } = [];
        public Task EnsureCollectionAsync(int dimensions, CancellationToken ct) => Task.CompletedTask;
        public Task UpsertAsync(IReadOnlyList<VectorRecord> points, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] vector, Guid userId, Guid? documentId, string generation, int limit, CancellationToken ct) { return Task.FromResult(Result); }
        public Task DeleteDocumentAsync(Guid documentId, CancellationToken ct) { return Task.CompletedTask; }
        public Task DeleteGenerationAsync(Guid documentId, string generation, CancellationToken ct) => Task.CompletedTask;
    }
}
