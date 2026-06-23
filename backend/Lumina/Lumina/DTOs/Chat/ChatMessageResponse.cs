namespace Lumina.DTOs.Chat;

public class ChatMessageResponse
{
    public Guid Id { get; set; }

    // "user" or "assistant".
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
