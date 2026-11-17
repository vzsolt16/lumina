namespace Lumina.DTOs.Chat;

public class ChatConversationResponse
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? ModelId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
