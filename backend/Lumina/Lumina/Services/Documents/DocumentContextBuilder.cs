using System.Text;
using System.Text.Json.Nodes;
using Lumina.Models;

namespace Lumina.Services.Documents;

public sealed class DocumentContextBuilder : IDocumentContextBuilder
{
    private readonly IDocumentContentConverter _converter;

    public DocumentContextBuilder(IDocumentContentConverter converter)
    {
        _converter = converter;
    }

    public Task<string> BuildAsync(
        Document document,
        DocumentContextType contextType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DocumentFormatDescriptor.UsesCanonicalSourceText(document.FileName, document.ContentJson))
        {
            var source = contextType switch
            {
                DocumentContextType.PlainText or DocumentContextType.TextWithIds => document.Content,
                DocumentContextType.StructureOutline => BuildSourceOutline(document),
                DocumentContextType.StructuredJson => throw new InvalidOperationException("Source-text documents do not have a ProseMirror JSON representation."),
                _ => throw new ArgumentOutOfRangeException(nameof(contextType))
            };
            return Task.FromResult(source);
        }
        var json = _converter.Normalize(document.ContentJson);

        var result = contextType switch
        {
            DocumentContextType.PlainText => _converter.ExtractPlainText(json),
            DocumentContextType.TextWithIds => BuildTextWithIds(json),
            DocumentContextType.StructuredJson => json,
            DocumentContextType.StructureOutline => BuildStructureOutline(json),
            _ => throw new ArgumentOutOfRangeException(nameof(contextType))
        };

        return Task.FromResult(result);
    }

    private static string BuildSourceOutline(Document document)
    {
        var lines = document.Content.Split('\n');
        if (!string.Equals(Path.GetExtension(document.FileName), ".md", StringComparison.OrdinalIgnoreCase))
            return $"Source text: {document.FileName}\nLine count: {lines.Length}";
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = System.Text.RegularExpressions.Regex.Match(lines[i], @"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$");
            if (match.Success) builder.Append(' ', (match.Groups[1].Length - 1) * 2).Append("- line ").Append(i).Append(": ").AppendLine(match.Groups[2].Value);
        }
        return builder.Length == 0 ? $"Markdown source: {document.FileName}\nNo heading lines. Total lines: {lines.Length}" : builder.ToString().TrimEnd();
    }

    private static string BuildTextWithIds(string contentJson)
    {
        var root = JsonNode.Parse(contentJson)!.AsObject();
        var builder = new StringBuilder();
        AppendTextWithIds(root, builder);
        return builder.ToString().TrimEnd();
    }

    private static void AppendTextWithIds(JsonObject node, StringBuilder builder)
    {
        var type = node["type"]?.GetValue<string>();
        if (type == "text")
        {
            builder.Append(node["text"]?.GetValue<string>());
            return;
        }

        var id = node["attrs"]?["id"]?.GetValue<string>();
        var children = node["content"] as JsonArray;
        if (type == "image")
        {
            builder.Append(string.IsNullOrWhiteSpace(id)
                ? "[image]"
                : $"[image id=\"{id}\"]");
            return;
        }

        if (id is not null && children is not null)
        {
            builder.Append('[').Append(id).Append("] ");
        }

        if (children is not null)
        {
            foreach (var child in children.OfType<JsonObject>())
            {
                AppendTextWithIds(child, builder);
            }
        }

        if (id is not null)
        {
            builder.AppendLine().AppendLine();
        }
    }

    private static string BuildStructureOutline(string contentJson)
    {
        var root = JsonNode.Parse(contentJson)!.AsObject();
        var builder = new StringBuilder();
        AppendOutline(root, builder, 0);
        return builder.ToString().TrimEnd();
    }

    private static void AppendOutline(JsonObject node, StringBuilder builder, int depth)
    {
        var type = node["type"]?.GetValue<string>() ?? "unknown";
        if (type == "text")
        {
            return;
        }

        var id = node["attrs"]?["id"]?.GetValue<string>();
        var text = _textForOutline(node);
        if (id is not null)
        {
            builder.Append(' ', depth * 2)
                .Append("- [").Append(id).Append("] ")
                .Append(type);
            if (type == "image" && node["attrs"] is JsonObject attrs)
            {
                var assetId = attrs["assetId"]?.GetValue<string>();
                var width = attrs["width"]?.GetValue<int?>();
                var height = attrs["height"]?.GetValue<int?>();
                if (!string.IsNullOrWhiteSpace(assetId))
                {
                    builder.Append(" (assetId: ").Append(assetId);
                    if (width is not null && height is not null)
                    {
                        builder.Append(", ").Append(width).Append("x").Append(height);
                    }
                    builder.Append(')');
                }
            }
            if (text.Length > 0)
            {
                builder.Append(": ").Append(text);
            }
            builder.AppendLine();
        }

        if (node["content"] is JsonArray children)
        {
            foreach (var child in children.OfType<JsonObject>())
            {
                AppendOutline(child, builder, depth + 1);
            }
        }
    }

    private static string _textForOutline(JsonObject node)
    {
        var builder = new StringBuilder();
        AppendInlineText(node, builder);
        var text = builder.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 160 ? text : text[..160].TrimEnd() + "…";
    }

    private static void AppendInlineText(JsonObject node, StringBuilder builder)
    {
        if (node["type"]?.GetValue<string>() == "text")
        {
            builder.Append(node["text"]?.GetValue<string>());
            return;
        }

        if (node["content"] is JsonArray children)
        {
            foreach (var child in children.OfType<JsonObject>())
            {
                AppendInlineText(child, builder);
            }
        }
    }
}
