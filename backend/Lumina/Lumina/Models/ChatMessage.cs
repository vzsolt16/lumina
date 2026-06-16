namespace Lumina.Models;

public class ChatMessage
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}