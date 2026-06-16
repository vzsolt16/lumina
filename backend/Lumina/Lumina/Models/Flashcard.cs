namespace Lumina.Models;

public class Flashcard
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;
}