namespace Lumina.Models;

public class QuizQuestion
{
    public Guid Id { get; set; }

    public Guid QuizId { get; set; }

    public Quiz Quiz { get; set; } = null!;

    public string Question { get; set; } = string.Empty;

    public string AnswerA { get; set; } = string.Empty;

    public string AnswerB { get; set; } = string.Empty;

    public string AnswerC { get; set; } = string.Empty;

    public string AnswerD { get; set; } = string.Empty;

    // "A", "B", "C", or "D"
    public string CorrectAnswer { get; set; } = string.Empty;
}