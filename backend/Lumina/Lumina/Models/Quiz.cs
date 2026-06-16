namespace Lumina.Models;

public class Quiz
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public ICollection<QuizQuestion> Questions { get; set; } = [];
}