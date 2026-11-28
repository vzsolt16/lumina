using Lumina.Data;
using Lumina.Models;
using Lumina.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumina.Tests;

public sealed class DocumentContextSelectorTests
{
    [Fact]
    public async Task SmallDocumentUsesFullContentWithoutEmbedding()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var document = NewDocument(userId, "Short enough", 1);
        db.Documents.Add(document); await db.SaveChangesAsync();
        var embeddings = new FakeEmbeddings(); var vectors = new FakeVectors();
        using var provider = CreateProvider(db);
        var selector = CreateSelector(provider.GetRequiredService<IServiceScopeFactory>(), embeddings, vectors, 8000);
        var result = await selector.SelectAsync(document.Id, userId, "Question", [], default);
        Assert.Equal("full", result.Mode);
        Assert.Equal("Short enough", result.Text);
        Assert.Equal(0, embeddings.QueryCalls);
    }

    [Fact]
    public async Task LargeDocumentWithStaleIndexReturnsPendingAndNeverSearchesOldGeneration()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var document = NewDocument(userId, new string('x', 20000), 2);
        document.IndexingStatus = "Indexed"; document.IndexedContentVersion = 1; document.ActiveIndexGeneration = "old";
        db.Documents.Add(document); await db.SaveChangesAsync();
        var vectors = new FakeVectors();
        using var provider = CreateProvider(db);
        var result = await CreateSelector(provider.GetRequiredService<IServiceScopeFactory>(), new FakeEmbeddings(), vectors, 1500)
            .SelectAsync(document.Id, userId, "Question", [], default);
        Assert.Equal("pending", result.Mode);
        Assert.Equal(0, vectors.SearchCalls);
    }

    [Fact]
    public async Task LargeCurrentDocumentSearchesWithAuthenticatedOwnerAndReturnsSources()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var document = NewDocument(userId, new string('x', 20000), 2);
        document.IndexingStatus = "Indexed"; document.IndexedContentVersion = 2; document.ActiveIndexGeneration = "active";
        document.ChunkingVersion = DocumentChunker.Version;
        db.Documents.Add(document); await db.SaveChangesAsync();
        var vectors = new FakeVectors { Result = [new VectorMatch(document.Id, 2, "active", new DocumentChunk(0, "relevant passage", "Topic", ["node-1"]), .5f)] };
        using var provider = CreateProvider(db);
        var result = await CreateSelector(provider.GetRequiredService<IServiceScopeFactory>(), new FakeEmbeddings(), vectors, 1500)
            .SelectAsync(document.Id, userId, "Question", [], default);
        Assert.Equal("rag", result.Mode);
        Assert.Equal(userId, vectors.LastUserId);
        Assert.Equal(document.Id, result.Sources.Single().DocumentId);
        Assert.Equal("Topic", result.Sources.Single().Heading);
        Assert.Contains("relevant passage", result.Text);
    }

    [Fact]
    public async Task FullContextPreservesIndexForLaterSemanticRetrieval()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var document = NewDocument(userId, new string('x', 20000), 2);
        document.IndexingStatus = "Indexed"; document.IndexedContentVersion = 2;
        document.ActiveIndexGeneration = "active"; document.ChunkingVersion = DocumentChunker.Version;
        db.Documents.Add(document); await db.SaveChangesAsync();
        var vectors = new FakeVectors
        {
            Result = [new VectorMatch(document.Id, 2, "active", new DocumentChunk(0, "relevant passage", "Topic", ["node-1"]), .5f)]
        };
        using var provider = CreateProvider(db);
        var selector = CreateSelector(provider.GetRequiredService<IServiceScopeFactory>(), new FakeEmbeddings(), vectors, 1500);

        var full = await selector.SelectAsync(document.Id, userId, "Question", [], default, evidenceBudgetTokens: 10000);
        var afterFull = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == document.Id);

        Assert.Equal("rag", full.Mode);
        Assert.Equal("Indexed", afterFull.IndexingStatus);
        Assert.Equal(2, afterFull.IndexedContentVersion);
        Assert.Equal("active", afterFull.ActiveIndexGeneration);
        Assert.Equal(0, vectors.DeleteDocumentCalls);

        var semantic = await selector.SelectAsync(document.Id, userId, "Question", [], default, evidenceBudgetTokens: 1500);

        Assert.Equal("rag", semantic.Mode);
        Assert.Equal(4, vectors.SearchCalls);
        Assert.Equal("active", vectors.LastGeneration);
        Assert.Contains("relevant passage", semantic.Text);
    }

    [Fact]
    public async Task FullContextLeavesUnindexedDocumentStateUntouched()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); var document = NewDocument(userId, "A document that fits", 1);
        document.IndexingStatus = "NotIndexed";
        db.Documents.Add(document); await db.SaveChangesAsync();
        var vectors = new FakeVectors();
        using var provider = CreateProvider(db);

        var result = await CreateSelector(provider.GetRequiredService<IServiceScopeFactory>(), new FakeEmbeddings(), vectors, 8000)
            .SelectAsync(document.Id, userId, "Question", [], default);

        var after = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        Assert.Equal("full", result.Mode);
        Assert.Equal("NotIndexed", after.IndexingStatus);
        Assert.Null(after.ActiveIndexGeneration);
        Assert.Equal(0, vectors.DeleteDocumentCalls);
        Assert.Equal(0, vectors.SearchCalls);
    }

    [Fact]
    public async Task LargeDocumentExceedingBudgetUsesRagInsteadOfFull()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid(); 
        // 60,000 characters is > 12,000 tokens based on (text.Length + 3) / 4 calculation = 15,000 tokens.
        var document = NewDocument(userId, new string('x', 60000), 2);
        document.IndexingStatus = "Indexed"; document.IndexedContentVersion = 2;
        document.ActiveIndexGeneration = "active"; document.ChunkingVersion = DocumentChunker.Version;
        db.Documents.Add(document); await db.SaveChangesAsync();
        
        var vectors = new FakeVectors { Result = [new VectorMatch(document.Id, 2, "active", new DocumentChunk(0, "relevant passage", "Topic", ["node-1"]), .5f)] };
        using var provider = CreateProvider(db);
        
        // Budget is 12000
        var selector = CreateSelector(provider.GetRequiredService<IServiceScopeFactory>(), new FakeEmbeddings(), vectors, 12000);
        var result = await selector.SelectAsync(document.Id, userId, "Question", [], default);
        
        // Should be "rag", not "full"
        Assert.Equal("rag", result.Mode);
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
        { ["Rag:ContextTokenBudget"] = budget.ToString() }).Build(), NullLogger<DocumentContextSelector>.Instance);

    private sealed class FakeEmbeddings : IEmbeddingService
    {
        public string Model => "test"; public int? Dimensions => 3; public int QueryCalls { get; private set; }
        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, string title, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[3]).ToArray());
        public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken) { QueryCalls++; return Task.FromResult(new float[3]); }
    }
    private sealed class FakeVectors : IVectorStore
    {
        public int SearchCalls { get; private set; } public int DeleteDocumentCalls { get; private set; }
        public Guid? LastUserId { get; private set; } public string? LastGeneration { get; private set; }
        public IReadOnlyList<VectorMatch> Result { get; init; } = [];
        public Task EnsureCollectionAsync(int dimensions, CancellationToken ct) => Task.CompletedTask;
        public Task UpsertAsync(IReadOnlyList<VectorRecord> points, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] vector, Guid userId, Guid? documentId, string generation, int limit, CancellationToken ct) { SearchCalls++; LastUserId = userId; LastGeneration = generation; return Task.FromResult(Result); }
        public Task DeleteDocumentAsync(Guid documentId, CancellationToken ct) { DeleteDocumentCalls++; return Task.CompletedTask; }
        public Task DeleteGenerationAsync(Guid documentId, string generation, CancellationToken ct) => Task.CompletedTask;
    }
}
