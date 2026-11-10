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
