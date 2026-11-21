using System.Text;
using System.Text.Json.Nodes;

namespace Lumina.Services.Rag;

public sealed record DocumentChunk(int Index, string Text, string? Heading, IReadOnlyList<string> NodeIds,
    IReadOnlyList<string>? HeadingPath = null, int StartBlockIndex = 0, int EndBlockIndex = 0, string? Format = null);

public interface IDocumentChunker
{
    IReadOnlyList<DocumentChunk> Chunk(string contentJson);
    IReadOnlyList<DocumentChunk> ChunkSourceText(string source, string extension);
}

/// <summary>
/// Tiptap block chunker. Token counts use a documented word-based approximation
/// (one token ~= 0.75 whitespace-delimited words); this is a sizing heuristic,
/// not a tokenizer. Chunk boundaries prefer headings and block boundaries.
/// </summary>
public sealed class DocumentChunker(IConfiguration configuration) : IDocumentChunker
{
    public const string Version = "format-aware-v3";
    private readonly int _chunkTokens = Math.Clamp(configuration.GetValue("Rag:ChunkTokens", 400), 100, 1000);
    private readonly int _overlapTokens = Math.Clamp(configuration.GetValue("Rag:OverlapTokens", 60), 0, 250);

    public IReadOnlyList<DocumentChunk> Chunk(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return [];
        var root = JsonNode.Parse(contentJson) as JsonObject;
        if (root is null) return [];
        var blocks = ReadBlocks(root);

        var output = new List<DocumentChunk>();
        var currentText = new StringBuilder();
        var currentHeading = (string?)null;
        var currentIds = new List<string>();
        IReadOnlyList<string> currentPath = [];
        var currentStart = 0; var currentEnd = 0;
        var currentTokens = 0;
        void Flush()
        {
            var text = currentText.ToString().Trim();
            if (text.Length > 0) output.Add(new(output.Count, text, currentHeading, currentIds.Distinct().ToArray(), currentPath, currentStart, currentEnd));
            currentText.Clear(); currentIds.Clear(); currentTokens = 0;
        }

        foreach (var block in blocks)
        {
            foreach (var part in SplitOversized(block.Text, _chunkTokens))
            {
                var count = CountTokens(part);
                if (currentTokens > 0 && (block.Heading != currentHeading || currentTokens + count > _chunkTokens))
                {
                    var prior = currentText.ToString();
                    var priorIds = currentIds.ToArray();
                    Flush();
                    // Carry a bounded textual tail across adjacent chunks; node IDs
                    // stay attached only to the original block chunk.
                    if (block.Heading == currentHeading && _overlapTokens > 0)
                    {
                        var overlap = TailTokens(prior, _overlapTokens);
                        if (overlap.Length > 0) { currentText.Append(overlap).Append("\n"); currentTokens = CountTokens(overlap); currentIds.AddRange(priorIds); }
                    }
                }
                if (currentText.Length == 0) { currentHeading = block.Heading; currentPath = block.Path; currentStart = block.Index; }
                if (currentText.Length > 0) currentText.AppendLine();
                currentText.Append(part);
                currentIds.AddRange(block.Ids);
                currentEnd = block.Index;
                currentTokens += count;
                if (currentTokens >= _chunkTokens)
                {
                    var completedText = currentText.ToString();
                    var completedIds = currentIds.ToArray();
                    Flush();
                    if (_overlapTokens > 0)
                    {
                        var overlap = TailTokens(completedText, _overlapTokens);
                        if (overlap.Length > 0) { currentText.Append(overlap).AppendLine(); currentTokens = CountTokens(overlap); currentIds.AddRange(completedIds); }
                    }
                }
            }
        }
        Flush();
        return output;
    }

    /// <summary>Chunks canonical source text without converting it to ProseMirror.</summary>
    public IReadOnlyList<DocumentChunk> ChunkSourceText(string source, string extension)
    {
        if (string.IsNullOrEmpty(source)) return [];
        var lines = source.Split('\n');
        var output = new List<DocumentChunk>();
        var headingStack = new List<(int Level, string Title)>();
        var current = new StringBuilder();
        var currentStart = 0;
        var currentEnd = 0;
        string? currentHeading = null;
        IReadOnlyList<string> currentPath = [];
        void Flush()
        {
            if (current.Length == 0) return;
            output.Add(new(output.Count, current.ToString(), currentHeading, [], currentPath, currentStart, currentEnd, extension.ToLowerInvariant()));
            current.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$");
                if (match.Success)
                {
                    Flush();
                    var level = match.Groups[1].Length;
                    while (headingStack.Count > 0 && headingStack[^1].Level >= level) headingStack.RemoveAt(headingStack.Count - 1);
                    headingStack.Add((level, match.Groups[2].Value));
                }
            }

            var path = headingStack.Select(h => h.Title).ToArray();
            var pieces = SplitOversized(line, _chunkTokens);
            foreach (var piece in pieces)
            {
                var nextTokens = CountTokens(piece);
                if (current.Length > 0 && !currentPath.SequenceEqual(path))
                    Flush();
                if (current.Length > 0 && CountTokens(current.ToString()) + nextTokens > _chunkTokens)
                    Flush();
                if (current.Length == 0)
                {
                    currentStart = i;
                    currentHeading = path.LastOrDefault();
                    currentPath = path;
                }
                if (current.Length > 0) current.Append('\n');
                current.Append(piece);
                currentEnd = i;
            }
        }
        Flush();
        return output;
    }

    private sealed record TextBlock(int Index, string Text, string? Heading, IReadOnlyList<string> Path, string[] Ids);
    private static List<TextBlock> ReadBlocks(JsonObject root)
    {
        var result = new List<TextBlock>(); var headings = new List<(int Level, string Title, string? Id)>();
        void Visit(JsonObject node)
        {
            var type = node["type"]?.GetValue<string>() ?? "";
            if (type == "heading")
            {
                var title = ReadText(node).Trim();
                if (title.Length > 0)
                {
                    var level = Math.Clamp(node["attrs"]?["level"]?.GetValue<int>() ?? 1, 1, 6);
                    while (headings.Count > 0 && headings[^1].Level >= level) headings.RemoveAt(headings.Count - 1);
                    headings.Add((level, title, node["attrs"]?["id"]?.GetValue<string>()));
                }
                return;
            }
            if (type is "paragraph" or "listItem" or "taskItem" or "codeBlock" or "tableCell" or "tableHeader")
            {
                var text = ReadText(node).Trim();
                if (text.Length == 0) return;
                var path = headings.Select(h => h.Title).ToArray();
                var ids = NodeIds(node).Concat(headings.Where(h => h.Id is not null).Select(h => h.Id!)).Distinct().ToArray();
                result.Add(new(result.Count, text, path.LastOrDefault(), path, ids));
                return;
            }
            if (node["content"] is JsonArray children)
            {
                // Recurse through containers while retaining text blocks as their original source units.
                if (children.OfType<JsonObject>().Any()) { foreach (var child in children.OfType<JsonObject>()) Visit(child); return; }
            }
            var fallbackText = ReadText(node).Trim();
            if (fallbackText.Length == 0) return;
            var fallbackPath = headings.Select(h => h.Title).ToArray();
            var fallbackIds = NodeIds(node).Concat(headings.Where(h => h.Id is not null).Select(h => h.Id!)).Distinct().ToArray();
            result.Add(new(result.Count, fallbackText, fallbackPath.LastOrDefault(), fallbackPath, fallbackIds));
        }
        if (root["content"] is JsonArray content) foreach (var node in content.OfType<JsonObject>()) Visit(node);
        return result;
    }

    private static IEnumerable<string> SplitOversized(string text, int limit)
    {
        if (CountTokens(text) <= limit) { yield return text; yield break; }
        var sentences = System.Text.RegularExpressions.Regex.Split(text, "(?<=[.!?])\\s+");
        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (CountTokens(sentence) > limit)
            {
                foreach (var word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (CountTokens(word) > limit)
                    {
                        if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                        // Last-resort approximate token split for very long URLs,
                        // identifiers, and unspaced text that has no word boundary.
                        var maxChars = Math.Max(4, limit * 4);
                        for (var offset = 0; offset < word.Length; offset += maxChars)
                            yield return word.Substring(offset, Math.Min(maxChars, word.Length - offset));
                        continue;
                    }
                    if (CountTokens(current.ToString()) + CountTokens(word) > limit && current.Length > 0)
                    { yield return current.ToString(); current.Clear(); }
                    if (current.Length > 0) current.Append(' ');
                    current.Append(word);
                }
                continue;
            }
            if (CountTokens(current.ToString()) + CountTokens(sentence) > limit && current.Length > 0)
            { yield return current.ToString(); current.Clear(); }
            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static string ReadText(JsonObject node)
    {
        if (node["type"]?.GetValue<string>() == "text") return node["text"]?.GetValue<string>() ?? "";
        var parts = new List<string>();
        if (node["content"] is JsonArray children)
            foreach (var child in children.OfType<JsonObject>()) parts.Add(ReadText(child));
        var type = node["type"]?.GetValue<string>();
        return type is "listItem" or "taskItem" ? "• " + string.Join(" ", parts) : string.Join(" ", parts);
    }

    private static IEnumerable<string> NodeIds(JsonObject node)
    {
        var id = node["attrs"]?["id"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(id)) yield return id;
        if (node["content"] is JsonArray children)
            foreach (var child in children.OfType<JsonObject>())
                foreach (var childId in NodeIds(child)) yield return childId;
    }

    public static int CountTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        // Blend the usual words-to-token heuristic with a character fallback so
        // long identifiers, URLs, or unspaced text are not treated as one token.
        return Math.Max(words * 4 / 3 + 1, (text.Length + 3) / 4);
    }
    private static string TailTokens(string text, int maxTokens)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var n = Math.Max(1, maxTokens * 3 / 4);
        return string.Join(' ', words.TakeLast(n));
    }
}
