using System.Text.Json;
using System.Text.Json.Nodes;
using Lumina.Models;

namespace Lumina.Services.Documents;

public sealed class ProseMirrorDocumentConverter : IDocumentContentConverter
{
    private static readonly HashSet<string> BlockNodeTypes = new(StringComparer.Ordinal)
    {
        "blockquote", "bulletList", "codeBlock", "heading", "listItem", "orderedList",
        "paragraph", "table", "tableCell", "tableHeader", "tableRow", "horizontalRule",
        "image"
    };

    public string CreateFromPlainText(string content)
    {
        DocumentValidation.ValidateEditableContent(content);

        var paragraphs = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => CreateParagraph(line))
            .ToArray();

        return Serialize(new JsonObject
        {
            ["type"] = "doc",
            ["content"] = new JsonArray(paragraphs)
        });
    }

    public string Normalize(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            throw new InvalidOperationException("Structured document content is required.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(contentJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Structured document content is invalid JSON.", ex);
        }

        if (root is not JsonObject document || document["type"]?.GetValue<string>() != "doc")
        {
            throw new InvalidOperationException("Structured document content must have a doc root node.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        NormalizeNode(document, ids, isRoot: true);
        return Serialize(document);
    }

    public string ExtractPlainText(string contentJson)
    {
        var normalized = Normalize(contentJson);
        var root = JsonNode.Parse(normalized)!.AsObject();
        var text = ExtractNodeText(root, isRoot: true);
        return text.TrimEnd('\n');
    }

    public string ExtractPlainText(Document document) =>
        string.IsNullOrWhiteSpace(document.ContentJson)
            ? document.Content
            : ExtractPlainText(document.ContentJson);

    private static JsonObject CreateParagraph(string text)
    {
        var paragraph = new JsonObject
        {
            ["type"] = "paragraph",
            ["attrs"] = new JsonObject
            {
                ["id"] = CreateNodeId()
            }
        };

        if (text.Length > 0)
        {
            paragraph["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = text
            });
        }

        return paragraph;
    }

    private static void NormalizeNode(JsonObject node, HashSet<string> ids, bool isRoot = false)
    {
        var type = node["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new InvalidOperationException("Every document node must have a type.");
        }

        if (!isRoot && BlockNodeTypes.Contains(type))
        {
            var attrs = node["attrs"] as JsonObject ?? [];
            var id = attrs["id"]?.GetValue<string>();
            if (!DocumentNodeIdPolicy.IsStableId(id) || !ids.Add(id!))
            {
                do
                {
                    id = CreateNodeId();
                } while (!ids.Add(id));
            }

            attrs["id"] = id;
            node["attrs"] = attrs;
        }

        if (node["content"] is JsonArray content)
        {
            foreach (var child in content)
            {
                if (child is not JsonObject childObject)
                {
                    throw new InvalidOperationException("Document content must contain JSON nodes.");
                }

                NormalizeNode(childObject, ids);
            }
        }
    }

    private static string ExtractNodeText(JsonObject node, bool isRoot = false)
    {
        var type = node["type"]?.GetValue<string>();
        if (type == "text")
        {
            return node["text"]?.GetValue<string>() ?? string.Empty;
        }

        if (type == "hardBreak")
        {
            return "\n";
        }

        var children = node["content"] as JsonArray;
        if (children is null)
        {
            return type == "image" || IsInlineContainer(type)
                ? string.Empty
                : (isRoot ? string.Empty : "\n");
        }

        var childText = children
            .OfType<JsonObject>()
            .Select(child => ExtractNodeText(child))
            .ToArray();

        if (isRoot)
        {
            return string.Join("\n", childText);
        }

        return IsInlineContainer(type)
            ? string.Concat(childText)
            : string.Join("\n", childText);
    }

    private static bool IsInlineContainer(string? type) =>
        type is "paragraph" or "heading" or "codeBlock" or "tableCell" or "tableHeader";

    private static string Serialize(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });

    private static string CreateNodeId() => $"node-{Guid.NewGuid():N}";
}
