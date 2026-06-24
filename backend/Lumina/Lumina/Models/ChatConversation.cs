namespace Lumina.Models;

// A single chat thread within a document. A document can have many conversations;
// the user can start new ones, continue old ones, or delete them. Ownership is
// inherited through the parent Document (Document → User).
public class ChatConversation
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    // Auto-derived from the first user message; "New chat" until then.
    public string Title { get; set; } = "New chat";

    public DateTime CreatedAt { get; set; }

    // Bumped on every new message so the list can sort most-recent-first.
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = [];
}
