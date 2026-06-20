namespace Lumina.DTOs.Flashcard;

public class FlashcardResponse
{
    public Guid Id { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;
}