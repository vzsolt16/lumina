using Lumina.DTOs.Quiz;
using Lumina.Models;

namespace Lumina.Services.Quizzes;

public interface IQuizService
{
    Task<QuizJob> CreateQuizJobAsync(Guid documentId, Guid userId);
    Task ProcessQuizJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task MarkJobFailedAsync(Guid jobId);
    Task<IReadOnlyList<QuizResponse>> GetAllAsync(Guid documentId, Guid userId);
    Task<QuizResponse?> GetByIdAsync(Guid documentId, Guid quizId, Guid userId);
    Task<bool> DeleteAsync(Guid documentId, Guid quizId, Guid userId);
}