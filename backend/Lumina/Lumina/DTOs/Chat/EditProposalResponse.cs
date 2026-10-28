namespace Lumina.DTOs.Chat;

// An AI-proposed document edit, attached to the assistant message that made it.
// MessageId is what the apply/reject endpoints are keyed by.
public class EditProposalResponse
{
    public Guid MessageId { get; set; }

    // The passage to replace, or — for an insert — the anchor to insert after
    // (empty when appending to the end of the document).
    public string Target { get; set; } = string.Empty;

    public string Replacement { get; set; } = string.Empty;

    // One of EditProposalStatus ("pending" | "applied" | "rejected").
    public string Status { get; set; } = string.Empty;

    // "replace" (rewrite the target) or "insert" (add Replacement after the
    // target, or at the end when the target is empty).
    public string Kind { get; set; } = "replace";
}
