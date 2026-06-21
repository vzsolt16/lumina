using Lumina.DTOs.Flashcard;
using Lumina.Models;

namespace Lumina.Services.Flashcards;

public interface IFlashcardService
{
    Task<FlashcardJob> CreateFlashcardJobAsync(Guid documentId, Guid userId);
    Task ProcessFlashcardJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task MarkJobFailedAsync(Guid jobId);
    Task<IReadOnlyList<FlashcardResponse>> GetAllAsync(Guid documentId, Guid userId);
    Task<FlashcardResponse?> GetByIdAsync(Guid documentId, Guid flashcardId, Guid userId);
    Task<bool> DeleteAsync(Guid documentId, Guid flashcardId, Guid userId);
}
