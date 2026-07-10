using Lumina.Services.Chat;

namespace Lumina.Tests;

public class EditProposalStreamFilterTests
{
    private static (string Visible, string Remainder, (string Target, string Replacement)? Proposal)
        Run(params string[] tokens)
    {
        var filter = new EditProposalStreamFilter();
        var visible = string.Concat(tokens.Select(filter.Push));
        var (remainder, proposal) = filter.Finish();
        return (visible, remainder, proposal);
    }

    // --- plain answers (no proposal) ---

    [Fact]
    public void Push_PlainProse_PassesEverythingThrough()
    {
        var (visible, remainder, proposal) = Run("Hello ", "there, ", "reader!");

        Assert.Equal("Hello there, reader!", visible + remainder);
        Assert.Null(proposal);
    }

    [Fact]
    public void Push_ProseWithAngleBrackets_IsNotSwallowed()
    {
        var (visible, remainder, proposal) = Run("a < b and x << y are ", "comparisons");

        Assert.Equal("a < b and x << y are comparisons", visible + remainder);
        Assert.Null(proposal);
    }

    [Fact]
    public void Finish_PartialMarkerPrefixAtEndOfStream_IsFlushedAsText()
    {
        var (visible, remainder, proposal) = Run("trailing ", "<<<EDIT_TAR");

        Assert.Equal("trailing <<<EDIT_TAR", visible + remainder);
        Assert.Null(proposal);
    }

    // --- well-formed proposals ---

    [Fact]
    public void Finish_WholeBlockInOneToken_ParsesProposal()
    {
        var (visible, remainder, proposal) = Run(
            "I'll fix that.\n<<<EDIT_TARGET>>>\nold text\n<<<EDIT_REPLACEMENT>>>\nnew text\n<<<EDIT_END>>>");

        Assert.Equal("I'll fix that.\n", visible);
        Assert.Equal("", remainder);
        Assert.Equal(("old text", "new text"), proposal);
    }

    [Fact]
    public void Finish_MarkersSplitAcrossTokens_ParsesProposal()
    {
        var (visible, remainder, proposal) = Run(
            "Sure. ", "<<<EDI", "T_TARGET>>", ">\nfoo bar", "\n<<<EDIT_REPLACE",
            "MENT>>>\nbaz qux\n", "<<<EDIT_", "END>>>");

        Assert.Equal("Sure. ", visible);
        Assert.Equal("", remainder);
        Assert.Equal(("foo bar", "baz qux"), proposal);
    }

    [Fact]
    public void Finish_MultilinePassages_ArePreservedInside()
    {
        var (_, _, proposal) = Run(
            "<<<EDIT_TARGET>>>\nline one\nline two\n<<<EDIT_REPLACEMENT>>>\nline A\n\nline B\n<<<EDIT_END>>>");

        Assert.Equal(("line one\nline two", "line A\n\nline B"), proposal);
    }

    [Fact]
    public void Finish_EmptyReplacement_MeansDeletion()
    {
        var (_, _, proposal) = Run(
            "<<<EDIT_TARGET>>>\ndelete me\n<<<EDIT_REPLACEMENT>>>\n<<<EDIT_END>>>");

        Assert.Equal(("delete me", ""), proposal);
    }

    [Fact]
    public void Finish_TrailingTextAfterEndMarker_ReturnsItAsRemainder()
    {
        var (visible, remainder, proposal) = Run(
            "<<<EDIT_TARGET>>>\na\n<<<EDIT_REPLACEMENT>>>\nb\n<<<EDIT_END>>>\nAnything else?");

        Assert.Equal("", visible);
        Assert.Equal("Anything else?", remainder);
        Assert.Equal(("a", "b"), proposal);
    }

    // --- malformed blocks fall back to visible text ---

    [Fact]
    public void Finish_StartMarkerWithoutEnd_FlushesHeldTextAsRemainder()
    {
        var (visible, remainder, proposal) = Run(
            "Here: ", "<<<EDIT_TARGET>>>\nsome text that never closes");

        Assert.Equal("Here: ", visible);
        Assert.Equal("<<<EDIT_TARGET>>>\nsome text that never closes", remainder);
        Assert.Null(proposal);
    }

    [Fact]
    public void Finish_EndBeforeReplacementMarker_IsRejected()
    {
        var (_, remainder, proposal) = Run(
            "<<<EDIT_TARGET>>>\na\n<<<EDIT_END>>>\n<<<EDIT_REPLACEMENT>>>\nb");

        Assert.Null(proposal);
        Assert.StartsWith("<<<EDIT_TARGET>>>", remainder);
    }

    [Fact]
    public void Push_NothingVisibleAfterStartMarker_UntilFinish()
    {
        var filter = new EditProposalStreamFilter();
        filter.Push("Reply.\n<<<EDIT_TARGET>>>\nsecret");

        var after = filter.Push(" more held text");

        Assert.Equal("", after);
    }
}
