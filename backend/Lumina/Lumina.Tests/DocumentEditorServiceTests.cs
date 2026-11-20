using System.Text.Json.Nodes;
using Lumina.Data;
using Lumina.Models;
using Lumina.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Tests;

public class DocumentEditorServiceTests
{
    [Fact]
    public async Task AppliesMultipleValidatedOperationsAtomicallyAndIncrementsVersionOnce()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var userId = Guid.NewGuid();
        var document = CreateDocument(userId, converter.CreateFromPlainText("First\nSecond"), converter);
        db.Documents.Add(document); await db.SaveChangesAsync();
        var root = JsonNode.Parse(document.ContentJson)!.AsObject();
        var firstId = root["content"]![0]!["attrs"]!["id"]!.GetValue<string>();
        var secondId = root["content"]![1]!["attrs"]!["id"]!.GetValue<string>();
        var editor = new DocumentEditorService(db, converter);

        var result = await editor.ApplyOperationsAsync(document.Id, userId,
        [
            new ReplaceDocumentNode(1, firstId, "Updated first"),
            new DeleteDocumentNode(1, secondId)
        ]);

        Assert.Equal("Updated first", result.Content);
        Assert.Equal(2, result.ContentVersion);
        Assert.Contains(firstId, result.ContentJson);
        Assert.DoesNotContain(secondId, result.ContentJson);
    }

    [Fact]
    public async Task InvalidLaterOperationLeavesEntireBatchUnapplied()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var userId = Guid.NewGuid();
        var document = CreateDocument(userId, converter.CreateFromPlainText("Original"), converter);
        db.Documents.Add(document); await db.SaveChangesAsync();
        var nodeId = JsonNode.Parse(document.ContentJson)!["content"]![0]!["attrs"]!["id"]!.GetValue<string>();
        var editor = new DocumentEditorService(db, converter);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => editor.ApplyOperationsAsync(document.Id, userId,
        [new ReplaceDocumentNode(1, nodeId, "Changed"), new DeleteDocumentNode(1, "missing-node")]));

        var unchanged = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        Assert.Equal("Original", unchanged.Content);
        Assert.Equal(1, unchanged.ContentVersion);
    }

    [Fact]
    public async Task ReplaceNode_PreservesNodeAttributesAndInlineMarks()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var userId = Guid.NewGuid();
        var document = CreateDocument(userId, """
            {
              "type": "doc",
              "content": [
                {"type": "heading", "attrs": {"id": "node-11111111111111111111111111111111", "level": 2}, "content": [{"type": "text", "text": "Old", "marks": [{"type": "bold"}]}]}
              ]
            }
            """, converter);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var service = new DocumentEditorService(db, converter);
        var result = await service.ApplyOperationAsync(
            document.Id,
            userId,
            new ReplaceDocumentNode(1, "node-11111111111111111111111111111111", "New"));

        var root = JsonNode.Parse(result.ContentJson)!.AsObject();
        var node = root["content"]![0]!.AsObject();
        Assert.Equal("New", node["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(2, node["attrs"]!["level"]!.GetValue<int>());
        Assert.Equal("bold", node["content"]![0]!["marks"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("New", result.Content);
        Assert.Equal(2, result.ContentVersion);
    }

    [Fact]
    public async Task InsertDeleteAndMove_PreserveStableIdsAndSynchronizePlainText()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var userId = Guid.NewGuid();
        var document = CreateDocument(userId, converter.CreateFromPlainText("One\nTwo"), converter);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var service = new DocumentEditorService(db, converter);
        var initial = JsonNode.Parse(document.ContentJson)!.AsObject();
        var firstId = initial["content"]![0]!["attrs"]!["id"]!.GetValue<string>();
        var secondId = initial["content"]![1]!["attrs"]!["id"]!.GetValue<string>();

        var inserted = await service.ApplyOperationAsync(
            document.Id,
            userId,
            new InsertDocumentNode(1, null, 1, "paragraph", "Inserted"));
        var insertedRoot = JsonNode.Parse(inserted.ContentJson)!.AsObject();
        var insertedId = insertedRoot["content"]![1]!["attrs"]!["id"]!.GetValue<string>();
        Assert.Equal("One\nInserted\nTwo", inserted.Content);

        var moved = await service.ApplyOperationAsync(
            document.Id,
            userId,
            new MoveDocumentNode(2, insertedId, null, 0));
        var movedRoot = JsonNode.Parse(moved.ContentJson)!.AsObject();
        Assert.Equal(insertedId, movedRoot["content"]![0]!["attrs"]!["id"]!.GetValue<string>());
        Assert.Equal(firstId, movedRoot["content"]![1]!["attrs"]!["id"]!.GetValue<string>());
        Assert.Equal("Inserted\nOne\nTwo", moved.Content);

        var deleted = await service.ApplyOperationAsync(
            document.Id,
            userId,
            new DeleteDocumentNode(3, secondId));
        Assert.Equal("Inserted\nOne", deleted.Content);
        Assert.DoesNotContain(secondId, deleted.ContentJson);
    }

    [Fact]
    public async Task ApplyOperation_RejectsStaleVersionAndOtherOwner()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var ownerId = Guid.NewGuid();
        var document = CreateDocument(ownerId, converter.CreateFromPlainText("Text"), converter);
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        var service = new DocumentEditorService(db, converter);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ApplyOperationAsync(
                document.Id,
                Guid.NewGuid(),
                new ReplaceDocumentNode(1, "missing", "Nope")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyOperationAsync(
                document.Id,
                ownerId,
                new ReplaceDocumentNode(2, "missing", "Nope")));
        Assert.Contains("changed", error.Message);
    }

    [Fact]
    public async Task MoveNode_UsesBlockDestinationAsInsertAfterAnchor()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var userId = Guid.NewGuid();
        var document = CreateDocument(userId, converter.CreateFromPlainText("One\nTwo\nThree"), converter);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var root = JsonNode.Parse(document.ContentJson)!.AsObject();
        var firstId = root["content"]![0]!["attrs"]!["id"]!.GetValue<string>();
        var secondId = root["content"]![1]!["attrs"]!["id"]!.GetValue<string>();
        var service = new DocumentEditorService(db, converter);

        var result = await service.ApplyOperationAsync(
            document.Id,
            userId,
            new MoveDocumentNode(1, firstId, secondId, 0));

        Assert.Equal("Two\nOne\nThree", result.Content);
    }

    [Fact]
    public async Task ValidateOperation_RejectsReplacingAListContainerWithRecoveryGuidance()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var userId = Guid.NewGuid();
        var document = CreateDocument(userId, converter.Normalize("""
            {
              "type": "doc",
              "content": [
                {
                  "type": "bulletList",
                  "attrs": {"id": "node-11111111111111111111111111111111"},
                  "content": [
                    {
                      "type": "listItem",
                      "attrs": {"id": "node-22222222222222222222222222222222"},
                      "content": [
                        {
                          "type": "paragraph",
                          "attrs": {"id": "node-33333333333333333333333333333333"},
                          "content": [{"type": "text", "text": "pear"}]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """), converter);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var service = new DocumentEditorService(db, converter);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateOperationAsync(
                document.Id,
                userId,
                new ReplaceDocumentNode(
                    1,
                    "node-11111111111111111111111111111111",
                    "1. pear")));

        Assert.Contains("bulletList", error.Message);
        Assert.Contains("move_document_node", error.Message);
    }

    [Fact]
    public async Task ChangeListType_ChangesOnlyTheListContainerAndPreservesItems()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var userId = Guid.NewGuid();
        var document = CreateDocument(userId, converter.Normalize("""
            {
              "type": "doc",
              "content": [
                {
                  "type": "orderedList",
                  "attrs": {"id": "node-11111111111111111111111111111111", "order": 1},
                  "content": [
                    {
                      "type": "listItem",
                      "attrs": {"id": "node-22222222222222222222222222222222"},
                      "content": [
                        {
                          "type": "paragraph",
                          "attrs": {"id": "node-33333333333333333333333333333333"},
                          "content": [{"type": "text", "text": "pear"}]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """), converter);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var service = new DocumentEditorService(db, converter);
        var result = await service.ApplyOperationAsync(
            document.Id,
            userId,
            new ChangeListType(
                1,
                "node-11111111111111111111111111111111",
                "bulletList"));

        var root = JsonNode.Parse(result.ContentJson)!.AsObject();
        var list = root["content"]![0]!.AsObject();
        Assert.Equal("bulletList", list["type"]!.GetValue<string>());
        Assert.Equal("node-11111111111111111111111111111111", list["attrs"]!["id"]!.GetValue<string>());
        Assert.Equal("node-22222222222222222222222222222222",
            list["content"]![0]!["attrs"]!["id"]!.GetValue<string>());
        Assert.Equal("pear", result.Content);
    }

    [Fact]
    public async Task ValidateOperation_RejectsSiblingPlacementForAListContainer()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var userId = Guid.NewGuid();
        var document = CreateDocument(userId, converter.Normalize("""
            {
              "type": "doc",
              "content": [
                {
                  "type": "orderedList",
                  "attrs": {"id": "node-11111111111111111111111111111111"},
                  "content": [
                    {
                      "type": "listItem",
                      "attrs": {"id": "node-22222222222222222222222222222222"},
                      "content": [
                        {
                          "type": "paragraph",
                          "attrs": {"id": "node-33333333333333333333333333333333"},
                          "content": [{"type": "text", "text": "pear"}]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """), converter);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var service = new DocumentEditorService(db, converter);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateOperationAsync(
                document.Id,
                userId,
                new MoveDocumentNode(
                    1,
                    "node-22222222222222222222222222222222",
                    "node-11111111111111111111111111111111",
                    0,
                    "after")));

        Assert.Contains("at_index", error.Message);
        Assert.Contains("container", error.Message);
    }

    [Fact]
    public async Task ValidateOperation_RejectsMovingNonListItemIntoAList()
    {
        await using var db = CreateDb();
        var converter = new ProseMirrorDocumentConverter();
        var userId = Guid.NewGuid();
        var document = CreateDocument(userId, converter.Normalize("""
            {
              "type": "doc",
              "content": [
                {
                  "type": "orderedList",
                  "attrs": {"id": "node-11111111111111111111111111111111"},
                  "content": [
                    {
                      "type": "listItem",
                      "attrs": {"id": "node-22222222222222222222222222222222"},
                      "content": [
                        {
                          "type": "paragraph",
                          "attrs": {"id": "node-33333333333333333333333333333333"},
                          "content": [{"type": "text", "text": "pear"}]
                        }
                      ]
                    }
                  ]
                },
                {
                  "type": "paragraph",
                  "attrs": {"id": "node-44444444444444444444444444444444"},
                  "content": [{"type": "text", "text": "outside"}]
                }
              ]
            }
            """), converter);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var service = new DocumentEditorService(db, converter);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateOperationAsync(
                document.Id,
                userId,
                new MoveDocumentNode(
                    1,
                    "node-44444444444444444444444444444444",
                    "node-11111111111111111111111111111111",
                    0,
                    "at_index")));

        Assert.Contains("listItem", error.Message);
        Assert.Contains("parent_id", error.Message);
    }

    private static LuminaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LuminaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LuminaDbContext(options);
    }

    private static Document CreateDocument(
        Guid userId,
        string contentJson,
        IDocumentContentConverter converter) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FileName = "test.txt",
            ContentJson = converter.Normalize(contentJson),
            Content = converter.ExtractPlainText(contentJson),
            ContentVersion = 1,
            ContentType = "text/plain",
            UploadedAt = DateTime.UtcNow
        };
}
