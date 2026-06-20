namespace Lumina.DTOs.Quiz;

public class QuizQuestionResponse
{
    public Guid Id { get; set; }

    public string Question { get; set; } = string.Empty;

    public string AnswerA { get; set; } = string.Empty;

    public string AnswerB { get; set; } = string.Empty;

    public string AnswerC { get; set; } = string.Empty;

    public string AnswerD { get; set; } = string.Empty;

    // "A", "B", "C", or "D"
    public string CorrectAnswer { get; set; } = string.Empty;
}