using System.Text.Json.Nodes;
using Lumina.Data;
using Lumina.Models;
using Lumina.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Tests;

public class DocumentEditorServiceTests
{
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
