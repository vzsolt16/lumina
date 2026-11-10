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
}
