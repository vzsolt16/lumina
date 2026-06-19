using Lumina.Models;

namespace Lumina.Services.Quizzes;

public interface IQuizService
{
    Task<QuizJob> CreateQuizJobAsync(Guid documentId, Guid userId);
    Task ProcessQuizJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Quiz>> GetAllAsync(Guid documentId, Guid userId);
    Task<Quiz?> GetByIdAsync(Guid documentId, Guid quizId, Guid userId);
    Task<bool> DeleteAsync(Guid documentId, Guid quizId, Guid userId);
}