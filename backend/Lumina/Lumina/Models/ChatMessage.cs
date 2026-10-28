namespace Lumina.Models;

public class ChatMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public ChatConversation Conversation { get; set; } = null!;

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // AI edit proposal (assistant messages only). The model can propose replacing
    // ProposalTarget (an exact passage of the document) with ProposalReplacement,
    // or — when ProposalKind is "insert" — inserting ProposalReplacement after
    // ProposalTarget (an empty target means append to the end). The user then
    // applies or rejects it. These stay null for ordinary messages; the document
    // itself is only touched on apply.
    public string? ProposalTarget { get; set; }

    public string? ProposalReplacement { get; set; }

    // One of EditProposalStatus; null when there is no proposal.
    public string? ProposalStatus { get; set; }

    // "replace" (or null, for pre-existing rows) or "insert". Distinguishes a
    // passage rewrite from an insertion/append at apply time.
    public string? ProposalKind { get; set; }
}