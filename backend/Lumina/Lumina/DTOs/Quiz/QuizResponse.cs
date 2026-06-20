namespace Lumina.DTOs.Quiz;

public class QuizResponse
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public List<QuizQuestionResponse> Questions { get; set; } = [];
}