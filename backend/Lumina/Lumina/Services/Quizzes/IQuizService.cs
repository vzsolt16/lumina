using Lumina.Models;

namespace Lumina.Services.Quizzes;

public interface IQuizService
{
    Task<QuizJob> CreateQuizJobAsync(Guid documentId);
    Task ProcessQuizJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Quiz>> GetAllAsync(Guid documentId);
    Task<Quiz?> GetByIdAsync(Guid documentId, Guid quizId);
    Task<bool> DeleteAsync(Guid documentId, Guid quizId);
}