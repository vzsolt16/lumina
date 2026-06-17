using Lumina.Models;

namespace Lumina.Services.Quizzes;

public interface IQuizService
{
    Task<QuizJob> CreateQuizJobAsync(Guid documentId);
    Task ProcessQuizJobAsync(Guid jobId, CancellationToken cancellationToken);
}