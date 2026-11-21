using Lumina.Data;
using Lumina.Models;
using Lumina.Services.Documents;
using Lumina.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumina.Tests;

public sealed class DocumentDeletionTests
{
    [Fact]
    public async Task DeletingDocumentRemovesItsVectors()
    {
        await using var db = new LuminaDbContext(new DbContextOptionsBuilder<LuminaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var userId = Guid.NewGuid();
        var document = new Document
        {
            Id = Guid.NewGuid(), UserId = userId, FileName = "notes.txt", Content = "content", ContentVersion = 1
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        var vectors = new RecordingVectorStore();
        var service = new DocumentService(db, null!, null!, null!, NullLogger<DocumentService>.Instance, null!, vectors);

        var deleted = await service.DeleteAsync(document.Id, userId);

        Assert.True(deleted);
        Assert.Equal(document.Id, vectors.DeletedDocumentId);
        Assert.False(await db.Documents.AnyAsync(d => d.Id == document.Id));
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public Guid? DeletedDocumentId { get; private set; }
        public Task EnsureCollectionAsync(int dimensions, CancellationToken ct) => Task.CompletedTask;
        public Task UpsertAsync(IReadOnlyList<VectorRecord> points, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<VectorMatch>> SearchAsync(float[] vector, Guid userId, Guid? documentId, string generation, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<VectorMatch>>([]);
        public Task DeleteDocumentAsync(Guid documentId, CancellationToken ct)
        {
            DeletedDocumentId = documentId;
            return Task.CompletedTask;
        }
        public Task DeleteGenerationAsync(Guid documentId, string generation, CancellationToken ct) => Task.CompletedTask;
    }
}
