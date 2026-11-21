using Lumina.Data;
using Lumina.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Rag;

public interface IDocumentIndexService { Task IndexAsync(Guid documentId, CancellationToken ct); }

public sealed class DocumentIndexService(LuminaDbContext db, IDocumentChunker chunker, IEmbeddingService embeddings,
    IVectorStore vectors, ILogger<DocumentIndexService> logger, IConfiguration config) : IDocumentIndexService
{
    public async Task IndexAsync(Guid documentId, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null) return;
        var version = document.ContentVersion;
        document.IndexingStatus = "Indexing"; document.IndexingError = null;
        await db.SaveChangesAsync(ct);
        var generation = Guid.NewGuid().ToString("N");
        try
        {
            // Keep an index for mid-sized documents as a fallback when request
            // history, tools, or a smaller configured model leaves little room.
            if (DocumentChunker.CountTokens(document.Content) <= config.GetValue("Rag:FullDocumentMaxTokens", 2000))
            {
                await vectors.DeleteDocumentAsync(document.Id, ct);
                document.ActiveIndexGeneration = null;
                document.IndexedContentVersion = null;
                document.IndexingStatus = "NotIndexed";
                document.IndexingError = null;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Skipped vector indexing for small document {DocumentId} version {Version}", documentId, version);
                return;
            }
            var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
            var chunks = DocumentFormatDescriptor.UsesCanonicalSourceText(document.FileName, document.ContentJson)
                ? chunker.ChunkSourceText(document.Content, extension)
                : chunker.Chunk(document.ContentJson);
            if (!DocumentFormatDescriptor.UsesCanonicalSourceText(document.FileName, document.ContentJson))
                chunks = chunks.Select(c => c with { Format = extension }).ToArray();
            if (chunks.Count > 0)
            {
                var vectorsOut = await embeddings.EmbedDocumentsAsync(chunks.Select(c =>
                    c.HeadingPath is { Count: > 0 }
                        ? $"{string.Join(" > ", c.HeadingPath)}\n{c.Text}"
                        : c.Text).ToArray(), document.FileName, ct);
                await vectors.EnsureCollectionAsync(vectorsOut[0].Length, ct);
                var records = chunks.Select((c, i) => new VectorRecord(Guid.NewGuid(), vectorsOut[i], document.UserId, document.Id, version, generation, c, embeddings.Model)).ToArray();
                await vectors.UpsertAsync(records, ct);
            }
            else
            {
                // Probe the configured model so the shared collection is initialized
                // even when the first indexed document is empty.
                var probe = await embeddings.EmbedQueryAsync("empty document", ct);
                await vectors.EnsureCollectionAsync(probe.Length, ct);
            }

            var latest = await db.Documents.AsNoTracking().Where(d => d.Id == documentId).Select(d => (int?)d.ContentVersion).SingleOrDefaultAsync(ct);
            if (latest != version)
            {
                await vectors.DeleteGenerationAsync(documentId, generation, ct);
                await db.Documents.Where(d => d.Id == documentId).ExecuteUpdateAsync(s => s.SetProperty(d => d.IndexingStatus, "Pending"), ct);
                logger.LogInformation("Discarded outdated document index {DocumentId} version {Version}; latest is {LatestVersion}", documentId, version, latest);
                return;
            }

            var oldGeneration = document.ActiveIndexGeneration;
            document.ActiveIndexGeneration = generation;
            document.IndexedContentVersion = version;
            document.EmbeddingModel = embeddings.Model;
            document.VectorDimensions = embeddings.Dimensions;
            document.ChunkingVersion = DocumentChunker.Version;
            document.LastIndexedAt = DateTime.UtcNow;
            document.IndexingStatus = "Indexed";
            document.IndexingError = null;
            await db.SaveChangesAsync(ct);
            if (!string.IsNullOrWhiteSpace(oldGeneration)) await vectors.DeleteGenerationAsync(documentId, oldGeneration, ct);
            logger.LogInformation("Indexed document {DocumentId} version {Version}: {ChunkCount} chunks in {DurationMs} ms", documentId, version, chunks.Count, started.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Indexing failed for document {DocumentId} version {Version}", documentId, version);
            var current = await db.Documents.SingleOrDefaultAsync(d => d.Id == documentId, CancellationToken.None);
            if (current is not null) { current.IndexingStatus = "Failed"; current.IndexingError = ex.Message[..Math.Min(ex.Message.Length, 1000)]; await db.SaveChangesAsync(CancellationToken.None); }
            throw;
        }
    }
}
