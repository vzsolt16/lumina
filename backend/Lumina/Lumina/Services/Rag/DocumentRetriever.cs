using Lumina.Data;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Rag;

public sealed record RagContext(string Mode, string Text, IReadOnlyList<Lumina.DTOs.Chat.ChatSource> Sources, int ApproxTokens);

public interface IDocumentContextSelector
{
    Task<RagContext> SelectAsync(Guid documentId, Guid userId, string question, IReadOnlyList<string> history, CancellationToken ct);
}

public sealed class DocumentContextSelector(IServiceScopeFactory scopes, IEmbeddingService embeddings,
    IVectorStore vectors, IConfiguration config, ILogger<DocumentContextSelector> logger) : IDocumentContextSelector
{
    public async Task<RagContext> SelectAsync(Guid documentId, Guid userId, string question, IReadOnlyList<string> history, CancellationToken ct)
    {
        Lumina.Models.Document? doc;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();
            doc = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, ct);
        }
        if (doc is null) throw new KeyNotFoundException("Document not found.");
        var budget = config.GetValue("Rag:ContextTokenBudget", 12000);
        // Reserve space for system policy/instructions and expected answer; history
        // is included in the estimate, using the same word/token approximation.
        var reserved = 5200 + history.Sum(DocumentChunker.CountTokens) + DocumentChunker.CountTokens(question);
        var fullText = doc.Content;
        var fullTokens = DocumentChunker.CountTokens(fullText) + reserved;
        if (string.IsNullOrWhiteSpace(fullText)) return new("none", "", [], reserved);
        if (fullTokens <= budget)
        {
            if (!string.IsNullOrWhiteSpace(doc.ActiveIndexGeneration) && doc.IndexingStatus != "Indexing")
            {
                await vectors.DeleteDocumentAsync(doc.Id, ct);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();
                await db.Documents.Where(d => d.Id == doc.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.ActiveIndexGeneration, (string?)null).SetProperty(d => d.IndexingStatus, "NotIndexed"), ct);
            }
            return new("full", fullText, [], fullTokens);
        }
        if (doc.IndexingStatus != "Indexed" || doc.IndexedContentVersion != doc.ContentVersion || doc.ActiveIndexGeneration is null)
            return new("pending", "Indexing is not current for this document. Avoid presenting old indexed content as current.", [], reserved);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<VectorMatch> matches;
        try
        {
            var queryVector = await embeddings.EmbedQueryAsync(question, ct);
            var topK = Math.Clamp(config.GetValue("Rag:TopK", 5), 1, 20);
            matches = await vectors.SearchAsync(queryVector, userId, documentId, doc.ActiveIndexGeneration, topK, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Retrieval failed for document {DocumentId} version {Version}", doc.Id, doc.ContentVersion);
            throw;
        }
        var selected = new List<VectorMatch>();
        var seen = new List<string>();
        foreach (var match in matches
            .Where(x => x.DocumentId == doc.Id && x.DocumentVersion == doc.ContentVersion && x.Generation == doc.ActiveIndexGeneration)
            .OrderByDescending(x => x.Score))
        {
            // A near-identical overlap chunk adds little context.
            var normalized = string.Join(' ', match.Chunk.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length == 0 || seen.Any(prior => HighlyOverlaps(prior, normalized))) continue;
            seen.Add(normalized);
            if (selected.Any(x => x.Chunk.Index == match.Chunk.Index)) continue;
            selected.Add(match);
        }
        var text = string.Join("\n\n", selected.Select(x => $"[Section: {x.Chunk.Heading ?? "(no heading)"}; chunk {x.Chunk.Index}]\n{x.Chunk.Text}"));
        var sources = selected.Select(x => new Lumina.DTOs.Chat.ChatSource(doc.Id, doc.FileName, x.Chunk.Heading, x.Chunk.Index, x.Chunk.NodeIds)).ToArray();
        logger.LogInformation("Retrieved {ChunkCount} chunks for document {DocumentId} in {DurationMs} ms", selected.Count, doc.Id, watch.ElapsedMilliseconds);
        return new("rag", text, sources, reserved + DocumentChunker.CountTokens(text));
    }

    private static bool HighlyOverlaps(string a, string b)
    {
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)) return true;
        var left = Shingles(a); var right = Shingles(b);
        if (left.Count == 0 || right.Count == 0) return false;
        var smaller = left.Count <= right.Count ? left : right;
        var larger = ReferenceEquals(smaller, left) ? right : left;
        return smaller.Count(x => larger.Contains(x)) / (double)smaller.Count > 0.65;
    }

    private static HashSet<string> Shingles(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 4 < words.Length; i++) result.Add(string.Join(' ', words.Skip(i).Take(5)));
        return result;
    }
}
