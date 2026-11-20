using System.Text.Json.Nodes;

namespace Lumina.Services.Rag;

public sealed record DocumentSectionMatch(string NodeId, string Title, IReadOnlyList<string> HeadingPath,
    string? ParentTitle, bool IsAmbiguous);
public sealed record DocumentSectionContent(Guid DocumentId, int DocumentVersion, string SectionNodeId,
    string Text, IReadOnlyList<string> NodeIds, bool Complete, string? NextNodeId);

/// <summary>Resolves and reads sections from a caller-supplied current canonical ProseMirror document.</summary>
public sealed class DocumentStructureRetriever
{
    private sealed record Entry(bool IsHeading, int Level, string? Id, string Text, IReadOnlyList<string> Path, string[] NodeIds);

    public IReadOnlyList<DocumentSectionMatch> FindSections(string contentJson, string titleQuery)
    {
        var entries = ReadEntries(contentJson);
        var candidates = entries.Where(e => e.IsHeading && !string.IsNullOrWhiteSpace(e.Id)
            && (e.Text.Contains(titleQuery, StringComparison.OrdinalIgnoreCase)
                || titleQuery.Contains(e.Text, StringComparison.OrdinalIgnoreCase))).ToArray();
        return candidates.Select(e => new DocumentSectionMatch(e.Id!, e.Text, e.Path,
            e.Path.Count > 1 ? e.Path[^2] : null, candidates.Length > 1)).ToArray();
    }

    public DocumentSectionContent? RetrieveSection(Guid documentId, int currentVersion, string contentJson,
        string sectionNodeId, int maxTokens)
    {
        var entries = ReadEntries(contentJson);
        var start = entries.FindIndex(e => e.IsHeading && e.Id == sectionNodeId);
        if (start < 0) return null;
        var level = entries[start].Level;
        var selected = new List<Entry>();
        for (var i = start; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (i > start && entry.IsHeading && entry.Level <= level) break;
            selected.Add(entry);
        }
        var output = new List<string>(); var ids = new List<string>(); var tokens = 0; string? next = null; var truncated = false;
        foreach (var entry in selected)
        {
            var rendered = entry.IsHeading ? $"{new string('#', Math.Clamp(entry.Level, 1, 6))} {entry.Text}" : entry.Text;
            var count = DocumentChunker.CountTokens(rendered);
            if (tokens + count > Math.Max(1, maxTokens)) { next = entry.Id ?? entry.NodeIds.FirstOrDefault(); truncated = true; break; }
            output.Add(rendered); tokens += count; ids.AddRange(entry.NodeIds);
        }
        return new(documentId, currentVersion, sectionNodeId, string.Join("\n\n", output), ids.Distinct().ToArray(),
            !truncated, next);
    }

    public IReadOnlyList<string> RetrieveNodes(string contentJson, IReadOnlyCollection<string> nodeIds)
    {
        var wanted = nodeIds.ToHashSet(StringComparer.Ordinal);
        return ReadEntries(contentJson).Where(e => e.NodeIds.Any(wanted.Contains) || (e.Id is not null && wanted.Contains(e.Id)))
            .Select(e => e.Text).Where(t => t.Length > 0).ToArray();
    }

    private static List<Entry> ReadEntries(string contentJson)
    {
        var root = JsonNode.Parse(contentJson) as JsonObject ?? throw new FormatException("Document content must be a ProseMirror object.");
        var entries = new List<Entry>(); var headings = new List<(int Level, string Title)>();
        void Visit(JsonObject node)
        {
            var type = node["type"]?.GetValue<string>() ?? "";
            if (type == "heading")
            {
                var title = Text(node).Trim(); if (title.Length == 0) return;
                var level = Math.Clamp(node["attrs"]?["level"]?.GetValue<int>() ?? 1, 1, 6);
                while (headings.Count > 0 && headings[^1].Level >= level) headings.RemoveAt(headings.Count - 1);
                headings.Add((level, title));
                var id = node["attrs"]?["id"]?.GetValue<string>();
                entries.Add(new(true, level, id, title, headings.Select(x => x.Title).ToArray(), id is null ? [] : [id]));
                return;
            }
            if (type is "paragraph" or "listItem" or "taskItem" or "codeBlock" or "tableCell" or "tableHeader")
            {
                var text = Text(node).Trim();
                if (text.Length > 0) entries.Add(new(false, 0, null, text, headings.Select(x => x.Title).ToArray(), Ids(node).ToArray()));
                return;
            }
            if (node["content"] is JsonArray children) foreach (var child in children.OfType<JsonObject>()) Visit(child);
        }
        if (root["content"] is JsonArray content) foreach (var child in content.OfType<JsonObject>()) Visit(child);
        return entries;
    }

    private static string Text(JsonObject node)
    {
        if (node["type"]?.GetValue<string>() == "text") return node["text"]?.GetValue<string>() ?? "";
        if (node["content"] is not JsonArray children) return "";
        return string.Join(" ", children.OfType<JsonObject>().Select(Text));
    }

    private static IEnumerable<string> Ids(JsonObject node)
    {
        var id = node["attrs"]?["id"]?.GetValue<string>(); if (!string.IsNullOrWhiteSpace(id)) yield return id;
        if (node["content"] is JsonArray children)
            foreach (var child in children.OfType<JsonObject>()) foreach (var nested in Ids(child)) yield return nested;
    }
}
