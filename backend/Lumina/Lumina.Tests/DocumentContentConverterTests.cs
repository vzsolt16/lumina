using System.Text.Json.Nodes;
using Lumina.Services.Documents;

namespace Lumina.Tests;

public class DocumentContentConverterTests
{
    private readonly ProseMirrorDocumentConverter _converter = new();

    [Fact]
    public void PlainText_RoundTripsThroughStructuredJson()
    {
        var json = _converter.CreateFromPlainText("Heading\n\nBody");

        Assert.Equal("Heading\n\nBody", _converter.ExtractPlainText(json));
        Assert.Equal(3, CountNodes(json, "paragraph"));
    }

    [Fact]
    public void Normalize_PreservesExistingIdsAndAddsMissingIds()
    {
        const string json = """
            {
              "type": "doc",
              "content": [
                {"type": "heading", "attrs": {"id": "node-11111111111111111111111111111111"}, "content": [{"type": "text", "text": "Title", "marks": [{"type": "bold"}]}]},
                {"type": "paragraph", "content": [{"type": "text", "text": "Body"}]}
              ]
            }
            """;

        var normalized = JsonNode.Parse(_converter.Normalize(json))!.AsObject();
        var nodes = normalized["content"]!.AsArray();

        Assert.Equal(
            "node-11111111111111111111111111111111",
            nodes[0]!["attrs"]!["id"]!.GetValue<string>());
        Assert.StartsWith("node-", nodes[1]!["attrs"]!["id"]!.GetValue<string>());
        Assert.Equal("bold", nodes[0]!["content"]![0]!["marks"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Normalize_ReplacesDuplicateIds()
    {
        const string json = """
            {
              "type": "doc",
              "content": [
                {"type": "paragraph", "attrs": {"id": "node-22222222222222222222222222222222"}, "content": [{"type": "text", "text": "One"}]},
                {"type": "paragraph", "attrs": {"id": "node-22222222222222222222222222222222"}, "content": [{"type": "text", "text": "Two"}]}
              ]
            }
            """;

        var normalized = JsonNode.Parse(_converter.Normalize(json))!.AsObject();
        var nodes = normalized["content"]!.AsArray();
        var firstId = nodes[0]!["attrs"]!["id"]!.GetValue<string>();
        var secondId = nodes[1]!["attrs"]!["id"]!.GetValue<string>();

        Assert.Equal("node-22222222222222222222222222222222", firstId);
        Assert.StartsWith("node-", secondId);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void ExtractPlainText_HandlesNestedListsAndHardBreaks()
    {
        const string json = """
            {
              "type": "doc",
              "content": [
                {"type": "heading", "content": [{"type": "text", "text": "Topic"}]},
                {"type": "bulletList", "content": [
                  {"type": "listItem", "content": [{"type": "paragraph", "content": [{"type": "text", "text": "First"}]}]},
                  {"type": "listItem", "content": [{"type": "paragraph", "content": [{"type": "text", "text": "Second"}, {"type": "hardBreak"}, {"type": "text", "text": "line"}]}]}
                ]}
              ]
            }
            """;

        Assert.Equal("Topic\nFirst\nSecond\nline", _converter.ExtractPlainText(json));
    }

    [Fact]
    public void Normalize_RejectsNonDocumentRoot()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            _converter.Normalize("""{"type":"paragraph"}"""));

        Assert.Contains("doc root", error.Message);
    }

    private static int CountNodes(string json, string type)
    {
        var root = JsonNode.Parse(json)!;
        return Enumerate(root)
            .Count(node => node["type"]?.GetValue<string>() == type);
    }

    private static IEnumerable<JsonObject> Enumerate(JsonNode node)
    {
        if (node is not JsonObject obj)
        {
            yield break;
        }

        yield return obj;
        if (obj["content"] is not JsonArray content)
        {
            yield break;
        }

        foreach (var child in content)
        {
            if (child is not null)
            {
                foreach (var descendant in Enumerate(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
