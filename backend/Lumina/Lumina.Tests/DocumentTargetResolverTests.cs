using Lumina.Services.Documents;

namespace Lumina.Tests;

public sealed class DocumentTargetResolverTests
{
    private const string Fixture = """{"type":"doc","content":[{"type":"heading","attrs":{"id":"h1","level":1},"content":[{"type":"text","text":"Trip"}]},{"type":"paragraph","attrs":{"id":"p1"},"content":[{"type":"text","text":"Getting There"}]},{"type":"heading","attrs":{"id":"h2","level":2},"content":[{"type":"text","text":"Getting There"}]},{"type":"paragraph","attrs":{"id":"p2"},"content":[{"type":"text","text":"Take the bus."}]},{"type":"heading","attrs":{"id":"h3","level":1},"content":[{"type":"text","text":"Other"}]},{"type":"paragraph","attrs":{"id":"p3"},"content":[{"type":"text","text":"Take the bus."}]}]}""";

    [Fact]
    public void ResolvesStableIdAndExactTextButReportsRepeatedTextAsAmbiguous()
    {
        var resolver = new DocumentTargetResolver();
        Assert.Equal(TargetResolutionStatus.Found, resolver.Resolve(Fixture, "p1").Status);
        Assert.Equal(TargetResolutionStatus.Ambiguous, resolver.Resolve(Fixture, "Take the bus.").Status);
        Assert.Equal(TargetResolutionStatus.NotFound, resolver.Resolve(Fixture, "missing").Status);
    }

    [Fact]
    public void ResolvesNestedSectionScopeAndRejectsTargetsOutsideIt()
    {
        var resolver = new DocumentTargetResolver();
        var matches = resolver.FindSections(Fixture, "Getting There");
        Assert.Single(matches);
        Assert.Equal(new[] { "Trip", "Getting There" }, matches[0].HeadingPath);
        Assert.Equal(TargetResolutionStatus.Found, resolver.Resolve(Fixture, "Take the bus.", "h2").Status);
        Assert.Equal(TargetResolutionStatus.NotFound, resolver.Resolve(Fixture, "Other", "h2").Status);
    }

    [Fact]
    public void RepeatedHeadingsRemainAmbiguousAndDeletedReferencesGoMissing()
    {
        var resolver = new DocumentTargetResolver();
        const string repeated = """{"type":"doc","content":[{"type":"heading","attrs":{"id":"one","level":1},"content":[{"type":"text","text":"Details"}]},{"type":"paragraph","attrs":{"id":"p1"},"content":[{"type":"text","text":"First."}]},{"type":"heading","attrs":{"id":"two","level":1},"content":[{"type":"text","text":"Details"}]}]}""";
        Assert.Equal(2, resolver.FindSections(repeated, "Details").Count);
        Assert.Equal(TargetResolutionStatus.NotFound, resolver.Resolve("""{"type":"doc","content":[]}""", "p1").Status);
    }
}
