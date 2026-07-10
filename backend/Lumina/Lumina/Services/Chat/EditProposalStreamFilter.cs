using System.Text;

namespace Lumina.Services.Chat;

// Separates the model's token stream into the visible reply and a trailing
// edit-proposal block (the marker format the prompt asks for — see
// ChatService.BuildPrompt). Raw text between markers instead of JSON because a
// small local model reliably copies multiline text verbatim but reliably
// mangles JSON string escaping.
//
// Tokens can split a marker anywhere ("<<<EDI" + "T_TARGET>>>"), so Push
// withholds any tail that could still grow into the start marker and releases
// it as soon as it can't. Once the start marker is seen, everything after it is
// withheld until Finish, which either parses a well-formed block or hands the
// withheld text back so nothing the model said is silently dropped.
internal sealed class EditProposalStreamFilter
{
    internal const string TargetMarker = "<<<EDIT_TARGET>>>";
    internal const string ReplacementMarker = "<<<EDIT_REPLACEMENT>>>";
    internal const string EndMarker = "<<<EDIT_END>>>";

    private readonly StringBuilder _held = new();
    private bool _capturing;

    // Feeds one streamed token in; returns the text now safe to show the user.
    public string Push(string token)
    {
        _held.Append(token);

        if (_capturing)
        {
            return string.Empty;
        }

        var text = _held.ToString();

        var start = text.IndexOf(TargetMarker, StringComparison.Ordinal);
        if (start >= 0)
        {
            _capturing = true;
            _held.Clear();
            _held.Append(text[start..]);
            return text[..start];
        }

        var keep = LongestSuffixThatPrefixesMarker(text);
        _held.Clear();
        _held.Append(text[^keep..]);
        return text[..^keep];
    }

    // Call once the stream has ended. Remainder is withheld text that turned out
    // not to be (part of) a proposal block; Proposal is set when the block parsed.
    public (string Remainder, (string Target, string Replacement)? Proposal) Finish()
    {
        var text = _held.ToString();
        _held.Clear();

        if (!_capturing)
        {
            return (text, null);
        }

        // _capturing means text starts with the full target marker.
        var replacementAt = text.IndexOf(ReplacementMarker, StringComparison.Ordinal);
        var endAt = text.IndexOf(EndMarker, StringComparison.Ordinal);

        if (replacementAt < 0 || endAt < 0 || endAt < replacementAt)
        {
            return (text, null);
        }

        var target = text[TargetMarker.Length..replacementAt].Trim();
        var replacement = text[(replacementAt + ReplacementMarker.Length)..endAt].Trim();
        var trailing = text[(endAt + EndMarker.Length)..].Trim();

        return (trailing, (target, replacement));
    }

    private static int LongestSuffixThatPrefixesMarker(string text)
    {
        var max = Math.Min(text.Length, TargetMarker.Length - 1);
        for (var length = max; length > 0; length--)
        {
            if (text.EndsWith(TargetMarker[..length], StringComparison.Ordinal))
            {
                return length;
            }
        }
        return 0;
    }
}
