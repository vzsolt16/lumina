namespace Lumina.Models;

public class ChatMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public ChatConversation Conversation { get; set; } = null!;

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // AI edit proposal (assistant messages only). Legacy text proposals can
    // replace ProposalTarget or insert ProposalReplacement after it; an empty
    // target means append, while "prepend" adds at the start. Structured node
    // proposals store one serialized operation; structured proposals store an
    // ordered operation list. The user then applies or rejects it.
    public string? ProposalTarget { get; set; }

    public string? ProposalReplacement { get; set; }

    // One of EditProposalStatus; null when there is no proposal.
    public string? ProposalStatus { get; set; }

    // Legacy kinds ("replace", "insert", "prepend") or "structured". Null is used by pre-existing rows.
    public string? ProposalKind { get; set; }

    // Serialized structured operation captured when the proposal was created.
    // The operation is validated and executed only after user approval.
    public string? ProposalOperationJson { get; set; }

    public int? ProposalContentVersion { get; set; }

    // Structured references used to generate this answer, retained for history/API reads.
    public string? SourcesJson { get; set; }
}
