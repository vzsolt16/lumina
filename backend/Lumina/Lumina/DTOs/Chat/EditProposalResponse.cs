namespace Lumina.DTOs.Chat;

// An AI-proposed document edit, attached to the assistant message that made it.
// MessageId is what the apply/reject endpoints are keyed by.
public class EditProposalResponse
{
    public Guid MessageId { get; set; }

    // The passage to replace, or — for a legacy insert — the anchor to insert
    // after. Empty for append/prepend proposals.
    public string Target { get; set; } = string.Empty;

    public string Replacement { get; set; } = string.Empty;

    // One of EditProposalStatus ("pending" | "applied" | "rejected").
    public string Status { get; set; } = string.Empty;

    // "replace", "insert", or "prepend".
    public string Kind { get; set; } = "replace";

    public int? ContentVersion { get; set; }
}
