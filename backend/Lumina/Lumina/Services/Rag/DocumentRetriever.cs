using Lumina.Data;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Rag;

public enum RetrievalOutcome { Complete, Partial, NoResults, IndexUnavailable, IndexStale, Indexing, Failed }
public sealed record RagContext(string Mode, string Text, IReadOnlyList<Lumina.DTOs.Chat.ChatSource> Sources, int ApproxTokens,
    RetrievalOutcome Outcome = RetrievalOutcome.Complete, bool Complete = true, string? Limitation = null,
    int? CandidateCount = null, int? AcceptedCandidateCount = null, int? RejectedCandidateCount = null);

public interface IDocumentContextSelector
{
    Task<RagContext> SelectAsync(Guid documentId, Guid userId, string question, IReadOnlyList<string> history,
        CancellationToken ct, int? evidenceBudgetTokens = null, bool forceSemantic = false);
}

public sealed class DocumentContextSelector(IServiceScopeFactory scopes, IEmbeddingService embeddings,
    IVectorStore vectors, IConfiguration config, ILogger<DocumentContextSelector> logger) : IDocumentContextSelector
{
    public async Task<RagContext> SelectAsync(Guid documentId, Guid userId, string question, IReadOnlyList<string> history,
        CancellationToken ct, int? evidenceBudgetTokens = null, bool forceSemantic = false)
    {
        var selectionWatch = System.Diagnostics.Stopwatch.StartNew();
        logger.LogDebug("Document context selection started for document {DocumentId}", documentId);
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
        if (string.IsNullOrWhiteSpace(fullText))
        {
            logger.LogInformation("Document context selection completed for document {DocumentId}: no content, {DurationMs} ms", doc.Id, selectionWatch.ElapsedMilliseconds);
            return new("none", "", [], reserved, RetrievalOutcome.NoResults);
        }
        if (!forceSemantic && (evidenceBudgetTokens is not null ? DocumentChunker.CountTokens(fullText) <= evidenceBudgetTokens : fullTokens <= budget))
        {
            logger.LogInformation("Document context selected: full document {DocumentId} version {Version} in {DurationMs} ms", doc.Id, doc.ContentVersion, selectionWatch.ElapsedMilliseconds);
            return new("full", fullText, [], fullTokens);
        }
        if (evidenceBudgetTokens is not null && !string.IsNullOrWhiteSpace(doc.ContentJson))
        {
            try
            {
                var structure = new DocumentStructureRetriever();
                var sections = structure.FindSections(doc.ContentJson, question);
                if (sections.Count > 1)
                {
                    var choices = string.Join("; ", sections.Select(s => string.Join(" > ", s.HeadingPath)));
                    return new("structural", $"More than one section matches the heading reference: {choices}. Ask the user which section they mean.", [], reserved,
                        RetrievalOutcome.Partial, false, "Section heading is ambiguous.");
                }
                if (sections.Count == 1)
                {
                    var section = sections[0];
                    var content = structure.RetrieveSection(doc.Id, doc.ContentVersion, doc.ContentJson, section.NodeId, evidenceBudgetTokens.Value);
                    if (content is not null)
                    {
                        var source = new Lumina.DTOs.Chat.ChatSource(doc.Id, doc.FileName, section.Title, -1,
                            content.NodeIds, section.HeadingPath, doc.ContentVersion, "structural", null, null, null, content.Complete);
                        var body = $"[Section: {string.Join(" > ", section.HeadingPath)}]\n{content.Text}";
                        if (!content.Complete) body += "\n[Section continues beyond this result; next node: " + (content.NextNodeId ?? "unidentified") + "].";
                        logger.LogInformation("Document context selected: structural section {DocumentId} version {Version}; complete {Complete}; {DurationMs} ms", doc.Id, doc.ContentVersion, content.Complete, selectionWatch.ElapsedMilliseconds);
                        return new("structural", body, [source], reserved + DocumentChunker.CountTokens(content.Text),
                            content.Complete ? RetrievalOutcome.Complete : RetrievalOutcome.Partial, content.Complete,
                            content.Complete ? null : "The selected section exceeds the remaining context budget.");
                    }
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidOperationException)
            {
                logger.LogWarning("Structural document retrieval could not parse document {DocumentId} version {Version}: {ErrorType}", doc.Id, doc.ContentVersion, ex.GetType().Name);
            }
        }
        if (doc.IndexingStatus != "Indexed" || doc.IndexedContentVersion != doc.ContentVersion
            || doc.ChunkingVersion != DocumentChunker.Version || doc.ActiveIndexGeneration is null)
        {
            var outcome = doc.IndexingStatus == "Indexing" || doc.IndexingStatus == "Pending" ? RetrievalOutcome.Indexing
                : doc.IndexedContentVersion is not null || doc.ActiveIndexGeneration is not null ? RetrievalOutcome.IndexStale
                : RetrievalOutcome.IndexUnavailable;
            logger.LogInformation("Document context selection completed for document {DocumentId}: semantic index unavailable ({Outcome}), {DurationMs} ms", doc.Id, outcome, selectionWatch.ElapsedMilliseconds);
            return new("pending", "The document index is not current. Do not treat indexed passages as current evidence.", [], reserved, outcome, false, "Current index unavailable.");
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<VectorMatch> matches;
        try
        {
            logger.LogDebug("Semantic retrieval started for document {DocumentId} version {Version}", doc.Id, doc.ContentVersion);
            var queryVector = await embeddings.EmbedQueryAsync(question, ct);
            var topK = Math.Clamp(config.GetValue("Rag:TopK", 5), 1, 20);
            matches = await vectors.SearchAsync(queryVector, userId, documentId, doc.ActiveIndexGeneration, topK, ct);
            logger.LogDebug("Vector search returned {ResultCount} candidates for document {DocumentId}", matches.Count, doc.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Retrieval failed for document {DocumentId} version {Version}", doc.Id, doc.ContentVersion);
            logger.LogInformation("Document context selection completed for document {DocumentId}: retrieval failed, {DurationMs} ms", doc.Id, selectionWatch.ElapsedMilliseconds);
            return new("failed", "Document retrieval failed; no indexed evidence is available for this answer.", [], reserved, RetrievalOutcome.Failed, false, "Vector retrieval failed.");
        }
        var selected = new List<VectorMatch>();
        var rejected = 0;
        var seen = new List<string>();
        foreach (var match in matches
            .Where(x => x.DocumentId == doc.Id && x.DocumentVersion == doc.ContentVersion && x.Generation == doc.ActiveIndexGeneration)
            .OrderByDescending(x => x.Score))
        {
            // A near-identical overlap chunk adds little context.
            var normalized = string.Join(' ', match.Chunk.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length == 0 || seen.Any(prior => HighlyOverlaps(prior, normalized))) { rejected++; continue; }
            seen.Add(normalized);
            if (selected.Any(x => x.Chunk.Index == match.Chunk.Index)) { rejected++; continue; }
            var evidenceBudget = evidenceBudgetTokens ?? budget;
            var nextTokens = DocumentChunker.CountTokens(match.Chunk.Text);
            if (selected.Sum(x => DocumentChunker.CountTokens(x.Chunk.Text)) + nextTokens > evidenceBudget) { rejected++; continue; }
            selected.Add(match);
        }
        var text = string.Join("\n\n", selected.Select(x => $"[Section: {x.Chunk.Heading ?? "(no heading)"}; chunk {x.Chunk.Index}]\n{x.Chunk.Text}"));
        var sources = selected.Select(x => new Lumina.DTOs.Chat.ChatSource(doc.Id, doc.FileName, x.Chunk.Heading,
            x.Chunk.Index, x.Chunk.NodeIds, x.Chunk.HeadingPath, x.DocumentVersion, "semantic", x.Score,
            x.Chunk.StartBlockIndex, x.Chunk.EndBlockIndex, true)).ToArray();
        logger.LogInformation("Evidence construction completed for document {DocumentId}: strategy semantic, accepted {AcceptedChunkCount}, rejected {RejectedChunkCount}, {DurationMs} ms", doc.Id, selected.Count, rejected, watch.ElapsedMilliseconds);
        var complete = selected.Count == matches.Count && selected.Count > 0;
        return new("rag", text, sources, reserved + DocumentChunker.CountTokens(text),
            selected.Count == 0 ? RetrievalOutcome.NoResults : complete ? RetrievalOutcome.Complete : RetrievalOutcome.Partial,
            complete, complete ? null : "Only some matching indexed passages fit the retrieval budget or available current matches.",
            matches.Count, selected.Count, Math.Max(0, matches.Count - selected.Count));
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
