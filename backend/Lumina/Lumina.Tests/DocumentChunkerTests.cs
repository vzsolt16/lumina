using Lumina.Services.Rag;
using Microsoft.Extensions.Configuration;

namespace Lumina.Tests;

public sealed class DocumentChunkerTests
{
    private static IDocumentChunker Create(int size = 400, int overlap = 60) => new DocumentChunker(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rag:ChunkTokens"] = size.ToString(), ["Rag:OverlapTokens"] = overlap.ToString()
        }).Build());

    [Fact]
    public void PreservesHeadingContextAndNodeIds()
    {
        var chunks = Create().Chunk("""{"type":"doc","content":[{"type":"heading","attrs":{"id":"h1"},"content":[{"type":"text","text":"Plants"}]},{"type":"paragraph","attrs":{"id":"p1"},"content":[{"type":"text","text":"Roots absorb water."}]},{"type":"paragraph","attrs":{"id":"p2"},"content":[{"type":"text","text":"Leaves use sunlight."}]}]}""");
        Assert.Single(chunks);
        Assert.Equal("Plants", chunks[0].Heading);
        Assert.Contains("Roots", chunks[0].Text);
        Assert.Contains("p1", chunks[0].NodeIds);
        Assert.Contains("p2", chunks[0].NodeIds);
    }

    [Fact]
    public void SupportsPlainTextAndContentWithoutHeadings()
    {
        var chunker = Create();
        var plain = chunker.Chunk("""{"type":"doc","content":[{"type":"paragraph","attrs":{"id":"node-a"},"content":[{"type":"text","text":"Plain text document."}]}]}""");
        Assert.Single(plain);
        Assert.Null(plain[0].Heading);
        Assert.Equal(new[] { "node-a" }, plain[0].NodeIds);
    }

    [Fact]
    public void SplitsOversizedParagraphIntoOrderedChunks()
    {
        var text = string.Join(' ', Enumerable.Range(0, 180).Select(i => $"word{i}."));
        var json = System.Text.Json.JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "paragraph", attrs = new { id = "p" }, content = new[] { new { type = "text", text } } } } });
        var chunks = Create(100, 10).Chunk(json);
        Assert.True(chunks.Count > 1);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Index));
        Assert.All(chunks, c => Assert.Contains("p", c.NodeIds));
    }

    [Fact]
    public void KeepsListItemsReadableAndGrouped()
    {
        var chunks = Create().Chunk("""{"type":"doc","content":[{"type":"bulletList","content":[{"type":"listItem","attrs":{"id":"l1"},"content":[{"type":"paragraph","content":[{"type":"text","text":"First item"}]}]},{"type":"listItem","attrs":{"id":"l2"},"content":[{"type":"paragraph","content":[{"type":"text","text":"Second item"}]}]}]}]}""");
        Assert.Contains("First item", chunks[0].Text);
        Assert.Contains("Second item", chunks[0].Text);
    }

    [Fact]
    public void PreservesNestedHeadingPathAndAncestorIds()
    {
        var chunks = Create().Chunk("""{"type":"doc","content":[{"type":"heading","attrs":{"id":"h1","level":1},"content":[{"type":"text","text":"Chapter"}]},{"type":"blockquote","attrs":{"id":"quote"},"content":[{"type":"heading","attrs":{"id":"h2","level":2},"content":[{"type":"text","text":"Nested"}]},{"type":"paragraph","attrs":{"id":"p"},"content":[{"type":"text","text":"Evidence."}]}]}]}""");
        var chunk = Assert.Single(chunks);
        Assert.Equal(new[] { "Chapter", "Nested" }, chunk.HeadingPath);
        Assert.Contains("h1", chunk.NodeIds);
        Assert.Contains("h2", chunk.NodeIds);
        Assert.Contains("p", chunk.NodeIds);
    }
}
