using Lumina.Services.Rag;

namespace Lumina.Tests;

public sealed class DocumentStructureRetrieverTests
{
    private const string Json = """{"type":"doc","content":[{"type":"heading","attrs":{"id":"h1","level":1},"content":[{"type":"text","text":"Intro"}]},{"type":"paragraph","attrs":{"id":"p1"},"content":[{"type":"text","text":"Opening facts."}]},{"type":"heading","attrs":{"id":"h2","level":2},"content":[{"type":"text","text":"Methods"}]},{"type":"paragraph","attrs":{"id":"p2"},"content":[{"type":"text","text":"First method details."}]},{"type":"heading","attrs":{"id":"h3","level":1},"content":[{"type":"text","text":"Methods"}]},{"type":"paragraph","attrs":{"id":"p3"},"content":[{"type":"text","text":"Second method details."}]}]}""";

    [Fact]
    public void FindsSectionsAndMarksRepeatedTitlesAmbiguous()
    {
        var retriever = new DocumentStructureRetriever();
        var found = retriever.FindSections(Json, "Methods");
        Assert.Equal(2, found.Count);
        Assert.All(found, section => Assert.True(section.IsAmbiguous));
        Assert.Equal("Intro", found[0].ParentTitle);
    }

    [Fact]
    public void RetrievesNestedSectionInOrderAndReportsBoundedCompleteness()
    {
        var retriever = new DocumentStructureRetriever();
        var full = retriever.RetrieveSection(Guid.NewGuid(), 7, Json, "h1", 100)!;
        Assert.True(full.Complete);
        Assert.Contains("First method details", full.Text);
        Assert.DoesNotContain("Second method details", full.Text);
        Assert.Contains("h2", full.NodeIds);

        var partial = retriever.RetrieveSection(Guid.NewGuid(), 7, Json, "h1", 3)!;
        Assert.False(partial.Complete);
        Assert.NotNull(partial.NextNodeId);
    }
}
