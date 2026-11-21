using System.Text.Json.Nodes;
using Lumina.Models;
using Lumina.Services.Documents;

namespace Lumina.Tests;

public class DocumentContextBuilderTests
{
    private readonly IDocumentContextBuilder _builder =
        new DocumentContextBuilder(new ProseMirrorDocumentConverter());

    [Fact]
    public async Task BuildsPlainTextWithStableIdsAndStructuredJson()
    {
        var converter = new ProseMirrorDocumentConverter();
        var json = converter.CreateFromPlainText("Title\nBody");
        var document = new Document
        {
            Content = "Title\nBody",
            ContentJson = json
        };

        var plain = await _builder.BuildAsync(document, DocumentContextType.PlainText);
        var withIds = await _builder.BuildAsync(document, DocumentContextType.TextWithIds);
        var structured = await _builder.BuildAsync(document, DocumentContextType.StructuredJson);

        Assert.Equal("Title\nBody", plain);
        Assert.Contains("[node-", withIds);
        Assert.Contains("Title", withIds);
        Assert.Equal("doc", JsonNode.Parse(structured)!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReturnsCanonicalSourceTextWithoutProseMirrorConversion()
    {
        const string source = "# Heading\r\n\r\n    indented()";
        var document = new Document { FileName = "sample.py", Content = source, ContentJson = "" };

        var text = await _builder.BuildAsync(document, DocumentContextType.PlainText);
        var withIds = await _builder.BuildAsync(document, DocumentContextType.TextWithIds);
        var outline = await _builder.BuildAsync(document, DocumentContextType.StructureOutline);

        Assert.Equal(source, text);
        Assert.Equal(source, withIds);
        Assert.Contains("Source text: sample.py", outline);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _builder.BuildAsync(document, DocumentContextType.StructuredJson));
    }

    [Fact]
    public async Task TextWithIdsIncludesNestedBlockIds()
    {
        var converter = new ProseMirrorDocumentConverter();
        var json = converter.Normalize("""
            {
              "type":"doc",
              "content":[
                {"type":"blockquote","content":[
                  {"type":"paragraph","content":[{"type":"text","text":"Nested"}]}
                ]}
              ]
            }
            """);
        var document = new Document { ContentJson = json };

        var result = await _builder.BuildAsync(document, DocumentContextType.TextWithIds);

        Assert.Equal(2, result.Split("[node-").Length - 1);
        Assert.Contains("Nested", result);
    }

    [Fact]
    public async Task StructureOutlineIncludesIdsTypesAndReadableTextWithoutFullJson()
    {
        var converter = new ProseMirrorDocumentConverter();
        var json = converter.Normalize("""
            {
              "type":"doc",
              "content":[
                {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Colors"}]},
                {"type":"paragraph","content":[{"type":"text","text":"Red and blue"}]}
              ]
            }
            """);
        var document = new Document { ContentJson = json };

        var result = await _builder.BuildAsync(document, DocumentContextType.StructureOutline);

        Assert.Contains("heading", result);
        Assert.Contains("Colors", result);
        Assert.Contains("[node-", result);
        Assert.DoesNotContain("\"content\"", result);
    }

    [Fact]
    public async Task StructureOutlineIncludesImageAssetMetadata()
    {
        var converter = new ProseMirrorDocumentConverter();
        var json = converter.Normalize("""
            {
              "type":"doc",
              "content":[
                {"type":"image","attrs":{"assetId":"asset-123","width":640,"height":480}}
              ]
            }
            """);
        var document = new Document { ContentJson = json };

        var result = await _builder.BuildAsync(document, DocumentContextType.StructureOutline);

        Assert.Contains("image", result);
        Assert.Contains("assetId: asset-123", result);
        Assert.Contains("640x480", result);
    }

    [Fact]
    public async Task FullTextContextIncludesImageNodeIdAtExactDocumentPosition()
    {
        var converter = new ProseMirrorDocumentConverter();
        var json = converter.Normalize("""
            {
              "type":"doc",
              "content":[
                {"type":"paragraph","attrs":{"id":"node-11111111111111111111111111111111"},"content":[{"type":"text","text":"Before"}]},
                {"type":"image","attrs":{"assetId":"asset-123"}},
                {"type":"paragraph","attrs":{"id":"node-33333333333333333333333333333333"},"content":[{"type":"text","text":"After"}]}
              ]
            }
            """);
        var document = new Document
        {
            ContentJson = json,
            Content = converter.ExtractPlainText(json)
        };

        var root = JsonNode.Parse(json)!["content"]!.AsArray();
        var imageId = root[1]!["attrs"]!["id"]!.GetValue<string>();
        var plain = await _builder.BuildAsync(document, DocumentContextType.PlainText);
        var withIds = await _builder.BuildAsync(document, DocumentContextType.TextWithIds);

        Assert.Equal($"Before\n[image id=\"{imageId}\"]\nAfter", plain);
        Assert.Contains($"[image id=\"{imageId}\"]", withIds);
        Assert.True(withIds.IndexOf("Before", StringComparison.Ordinal)
            < withIds.IndexOf(imageId, StringComparison.Ordinal));
        Assert.True(withIds.IndexOf(imageId, StringComparison.Ordinal)
            < withIds.IndexOf("After", StringComparison.Ordinal));
        Assert.DoesNotContain("asset-123", plain);
    }
}
