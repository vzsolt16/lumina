using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumina.Data;
using Lumina.DTOs.Document;
using Lumina.Models;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Documents;

public sealed class DocumentEditorService : IDocumentEditorService
{
    private static readonly HashSet<string> InsertableTypes = new(StringComparer.Ordinal)
    {
        "blockquote", "codeBlock", "heading", "paragraph"
    };

    private readonly LuminaDbContext _db;
    private readonly IDocumentContentConverter _converter;

    public DocumentEditorService(
        LuminaDbContext db,
        IDocumentContentConverter converter)
    {
        _db = db;
        _converter = converter;
    }

    public async Task<DocumentDetailResponse> ApplyOperationAsync(
        Guid documentId,
        Guid userId,
        DocumentOperation operation,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("Document not found.");

        if (document.ContentVersion != operation.ExpectedContentVersion)
        {
            throw new InvalidOperationException(
                "The document changed before this operation was applied. Reload it and try again.");
        }

        var root = JsonNode.Parse(_converter.Normalize(document.ContentJson))!.AsObject();
        ApplyOperation(root, operation);

        var normalized = _converter.Normalize(root.ToJsonString());
        document.ContentJson = normalized;
        document.Content = _converter.ExtractPlainText(normalized);
        document.ContentSize = Encoding.UTF8.GetByteCount(document.Content);
        document.ContentVersion++;
        document.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException(
                "The document changed before this operation was applied. Reload it and try again.",
                ex);
        }

        return ToResponse(document);
    }

    private static void ApplyOperation(JsonObject root, DocumentOperation operation)
    {
        switch (operation)
        {
            case ReplaceDocumentNode replace:
                ReplaceNode(root, replace);
                break;
            case InsertDocumentNode insert:
                InsertNode(root, insert);
                break;
            case DeleteDocumentNode delete:
                DeleteNode(root, delete.TargetId);
                break;
            case MoveDocumentNode move:
                MoveNode(root, move);
                break;
            default:
                throw new InvalidOperationException("Unsupported document operation.");
        }
    }

    private static void ReplaceNode(JsonObject root, ReplaceDocumentNode operation)
    {
        var target = FindNode(root, operation.TargetId, out _, out _)
            ?? throw new KeyNotFoundException("Document node not found.");
        var type = target["type"]?.GetValue<string>();

        if (type is not ("paragraph" or "heading" or "codeBlock"))
        {
            throw new InvalidOperationException(
                "Only paragraph, heading, and code block nodes support text replacement.");
        }

        var marks = FindFirstTextNode(target)?["marks"]?.DeepClone();
        var content = new JsonArray();
        if (operation.NewText.Length > 0)
        {
            var text = new JsonObject
            {
                ["type"] = "text",
                ["text"] = operation.NewText
            };
            if (marks is not null)
            {
                text["marks"] = marks;
            }

            content.Add(text);
        }

        target["content"] = content;
    }

    private static void InsertNode(JsonObject root, InsertDocumentNode operation)
    {
        if (!InsertableTypes.Contains(operation.NodeType))
        {
            throw new InvalidOperationException($"Node type '{operation.NodeType}' cannot be inserted.");
        }

        var parent = operation.ParentId is null
            ? root
            : FindNode(root, operation.ParentId, out _, out _)
                ?? throw new KeyNotFoundException("Parent node not found.");
        var content = parent["content"] as JsonArray ?? throw new InvalidOperationException(
            "The requested parent cannot contain block nodes.");

        if (operation.Index < 0 || operation.Index > content.Count)
        {
            throw new InvalidOperationException("The insertion index is outside the parent content.");
        }

        var node = new JsonObject
        {
            ["type"] = operation.NodeType,
            ["attrs"] = CreateAttributes(operation.Attributes),
            ["content"] = CreateTextContent(operation.Content)
        };
        content.Insert(operation.Index, node);
    }

    private static void DeleteNode(JsonObject root, string targetId)
    {
        var target = FindNode(root, targetId, out var parent, out var index)
            ?? throw new KeyNotFoundException("Document node not found.");
        if (parent is null || index < 0)
        {
            throw new InvalidOperationException("The document root cannot be deleted.");
        }

        (parent["content"] as JsonArray)!.RemoveAt(index);
    }

    private static void MoveNode(JsonObject root, MoveDocumentNode operation)
    {
        var target = FindNode(root, operation.TargetId, out var oldParent, out var oldIndex)
            ?? throw new KeyNotFoundException("Document node not found.");
        if (oldParent is null || oldIndex < 0)
        {
            throw new InvalidOperationException("The document root cannot be moved.");
        }

        JsonObject? anchorParent = null;
        var anchorIndex = -1;
        var newParent = operation.ParentId is null
            ? root
            : FindNode(root, operation.ParentId, out anchorParent, out anchorIndex)
                ?? throw new KeyNotFoundException("Parent node not found.");

        // A block node destination is an anchor; container destinations retain
        // explicit parent/index semantics.
        var insertionIndex = operation.Index;
        if (!CanContainBlockChildren(newParent) && anchorParent is not null)
        {
            newParent = anchorParent;
            insertionIndex = operation.Placement == "before"
                ? anchorIndex
                : anchorIndex + 1;
        }
        else if (operation.Placement is not ("at_index" or "before" or "after"))
        {
            throw new InvalidOperationException("The move placement is invalid.");
        }

        if (ReferenceEquals(target, newParent) || ContainsNode(target, newParent))
        {
            throw new InvalidOperationException("A node cannot be moved into itself or its descendants.");
        }

        var oldContent = (oldParent["content"] as JsonArray)!;
        oldContent.RemoveAt(oldIndex);

        var newContent = newParent["content"] as JsonArray ?? throw new InvalidOperationException(
            "The requested parent cannot contain block nodes.");
        var index = insertionIndex;
        if (ReferenceEquals(oldParent, newParent) && index > oldIndex)
        {
            index--;
        }

        if (index < 0 || index > newContent.Count)
        {
            throw new InvalidOperationException("The move index is outside the parent content.");
        }

        newContent.Insert(index, target);
    }

    private static JsonObject CreateAttributes(IReadOnlyDictionary<string, JsonElement>? attributes)
    {
        var result = new JsonObject
        {
            ["id"] = $"node-{Guid.NewGuid():N}"
        };

        if (attributes is null)
        {
            return result;
        }

        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.Key, "id", StringComparison.Ordinal))
            {
                continue;
            }

            result[attribute.Key] = JsonNode.Parse(attribute.Value.GetRawText());
        }

        return result;
    }

    private static JsonArray CreateTextContent(string content)
    {
        if (content.Length == 0)
        {
            return [];
        }

        return
        [
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = content
            }
        ];
    }

    private static JsonObject? FindNode(
        JsonObject node,
        string targetId,
        out JsonObject? parent,
        out int index)
    {
        parent = null;
        index = -1;
        if (HasId(node, targetId))
        {
            return node;
        }

        if (node["content"] is not JsonArray content)
        {
            return null;
        }

        for (var i = 0; i < content.Count; i++)
        {
            if (content[i] is not JsonObject child)
            {
                continue;
            }

            var found = FindNode(child, targetId, out var childParent, out var childIndex);
            if (found is null)
            {
                continue;
            }

            if (ReferenceEquals(found, child))
            {
                parent = node;
                index = i;
            }
            else
            {
                parent = childParent;
                index = childIndex;
            }

            return found;
        }

        return null;
    }

    private static JsonObject? FindFirstTextNode(JsonObject node)
    {
        if (node["type"]?.GetValue<string>() == "text")
        {
            return node;
        }

        if (node["content"] is not JsonArray content)
        {
            return null;
        }

        foreach (var child in content.OfType<JsonObject>())
        {
            var found = FindFirstTextNode(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool ContainsNode(JsonObject ancestor, JsonObject candidate)
    {
        if (ancestor["content"] is not JsonArray content)
        {
            return false;
        }

        return content.OfType<JsonObject>().Any(child =>
            ReferenceEquals(child, candidate) || ContainsNode(child, candidate));
    }

    private static bool CanContainBlockChildren(JsonObject node)
    {
        var type = node["type"]?.GetValue<string>();
        return type is "doc"
            or "blockquote"
            or "bulletList"
            or "listItem"
            or "orderedList"
            or "table"
            or "tableRow";
    }

    private static bool HasId(JsonObject node, string id) =>
        string.Equals(
            node["attrs"]?["id"]?.GetValue<string>(),
            id,
            StringComparison.Ordinal);

    private static DocumentDetailResponse ToResponse(Document document) => new()
    {
        Id = document.Id,
        FileName = document.FileName,
        FileSize = document.FileSize,
        ContentSize = document.ContentSize,
        ContentVersion = document.ContentVersion,
        UploadedAt = document.UploadedAt,
        UpdatedAt = document.UpdatedAt,
        Content = document.Content,
        ContentJson = document.ContentJson
    };
}
