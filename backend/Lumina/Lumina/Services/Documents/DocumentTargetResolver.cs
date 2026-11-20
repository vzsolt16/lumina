using System.Text.Json.Nodes;

namespace Lumina.Services.Documents;

public enum TargetResolutionStatus { Found, NotFound, Ambiguous }
public sealed record DocumentTarget(string Id, string Type, string Text, IReadOnlyList<string> HeadingPath,
    string? ParentId, int Index, int HeadingLevel = 0);
public sealed record TargetResolution(TargetResolutionStatus Status, IReadOnlyList<DocumentTarget> Matches);

/// <summary>Resolves edit references against the current canonical ProseMirror JSON.</summary>
public sealed class DocumentTargetResolver
{
    private static readonly HashSet<string> TextBlocks = ["paragraph", "heading", "codeBlock"];

    public TargetResolution Resolve(string contentJson, string reference, string? scopeSectionId = null)
    {
        var nodes = ReadTargets(contentJson);
        HashSet<string>? sectionNodes = null;
        if (scopeSectionId is not null) sectionNodes = GetSectionNodeIds(contentJson, scopeSectionId).ToHashSet(StringComparer.Ordinal);
        var byId = nodes.Where(n => IdEquals(n.Id, reference)
            && (sectionNodes is null || sectionNodes.Contains(n.Id))).ToArray();
        var matches = byId.Length > 0 ? byId : nodes.Where(n => TextBlocks.Contains(n.Type)
            && Normalize(n.Text).Equals(Normalize(reference), StringComparison.OrdinalIgnoreCase)
            && (sectionNodes is null || sectionNodes.Contains(n.Id))).ToArray();
        return matches.Length switch
        {
            0 => new(TargetResolutionStatus.NotFound, []),
            1 => new(TargetResolutionStatus.Found, matches),
            _ => new(TargetResolutionStatus.Ambiguous, matches)
        };
    }

    public IReadOnlyList<DocumentTarget> FindSections(string contentJson, string reference)
    {
        var sections = ReadTargets(contentJson).Where(n => n.Type == "heading"
            && (n.Text.Contains(reference, StringComparison.OrdinalIgnoreCase)
                || reference.Contains(n.Text, StringComparison.OrdinalIgnoreCase))).ToArray();
        return sections;
    }

    public IReadOnlyList<string> GetSectionNodeIds(string contentJson, string sectionId)
    {
        var nodes = ReadTargets(contentJson);
        var headingIndex = Array.FindIndex(nodes, n => n.Type == "heading" && IdEquals(n.Id, sectionId));
        if (headingIndex < 0) return [];
        var level = nodes[headingIndex].HeadingLevel;
        var result = new List<string>();
        for (var i = headingIndex; i < nodes.Length; i++)
        {
            if (i > headingIndex && nodes[i].Type == "heading" && nodes[i].HeadingLevel <= level) break;
            result.Add(nodes[i].Id);
        }
        return result;
    }

    public IReadOnlyList<DocumentTarget> GetSectionTargets(string contentJson, string sectionId)
    {
        var nodes = ReadTargets(contentJson);
        var headingIndex = Array.FindIndex(nodes, n => n.Type == "heading" && IdEquals(n.Id, sectionId));
        if (headingIndex < 0) return [];
        var level = nodes[headingIndex].HeadingLevel;
        var result = new List<DocumentTarget>();
        for (var i = headingIndex; i < nodes.Length; i++)
        {
            if (i > headingIndex && nodes[i].Type == "heading" && nodes[i].HeadingLevel <= level) break;
            result.Add(nodes[i]);
        }
        return result;
    }

    public DocumentTarget? FindById(string contentJson, string id) =>
        ReadTargets(contentJson).FirstOrDefault(node => IdEquals(node.Id, id));

    private static DocumentTarget[] ReadTargets(string contentJson)
    {
        var root = JsonNode.Parse(contentJson) as JsonObject ?? throw new FormatException("Document content is not valid ProseMirror JSON.");
        var result = new List<DocumentTarget>(); var headings = new List<(int Level, string Text)>();
        void Visit(JsonObject node, JsonObject parent, int index)
        {
            var type = node["type"]?.GetValue<string>() ?? "";
            var id = node["attrs"]?["id"]?.GetValue<string>();
            var text = ReadText(node).Trim();
            if (type == "heading")
            {
                var level = Math.Clamp(node["attrs"]?["level"]?.GetValue<int>() ?? 1, 1, 6);
                while (headings.Count > 0 && headings[^1].Level >= level) headings.RemoveAt(headings.Count - 1);
                headings.Add((level, text));
                if (!string.IsNullOrWhiteSpace(id)) result.Add(new(id, type, text, headings.Select(x => x.Text).ToArray(), ParentId(parent), index, level));
                return;
            }
            if (TextBlocks.Contains(type))
            {
                if (!string.IsNullOrWhiteSpace(id)) result.Add(new(id, type, text, headings.Select(x => x.Text).ToArray(), ParentId(parent), index));
                return;
            }
            if (!string.IsNullOrWhiteSpace(id))
                result.Add(new(id, type, text, headings.Select(x => x.Text).ToArray(), ParentId(parent), index));
            if (node["content"] is JsonArray children)
                for (var childIndex = 0; childIndex < children.Count; childIndex++)
                    if (children[childIndex] is JsonObject child) Visit(child, node, childIndex);
        }
        if (root["content"] is JsonArray content)
            for (var i = 0; i < content.Count; i++) if (content[i] is JsonObject child) Visit(child, root, i);
        return result.ToArray();
    }

    private static string? ParentId(JsonObject parent) => parent["attrs"]?["id"]?.GetValue<string>();
    private static bool IdEquals(string left, string right) => left == right || left == WithoutPrefix(right) || WithoutPrefix(left) == WithoutPrefix(right);
    private static string WithoutPrefix(string value) => value.StartsWith("node-", StringComparison.Ordinal) ? value[5..] : value;
    private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string ReadText(JsonObject node)
    {
        if (node["type"]?.GetValue<string>() == "text") return node["text"]?.GetValue<string>() ?? "";
        return node["content"] is JsonArray children
            ? string.Join(' ', children.OfType<JsonObject>().Select(ReadText))
            : "";
    }
}
