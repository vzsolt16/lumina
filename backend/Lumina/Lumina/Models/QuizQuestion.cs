namespace Lumina.Models;

public class QuizQuestion
{
    public Guid Id { get; set; }

    public Guid QuizId { get; set; }

    public Quiz Quiz { get; set; } = null!;

    public string Question { get; set; } = string.Empty;

    public string CorrectAnswer { get; set; } = string.Empty;
}