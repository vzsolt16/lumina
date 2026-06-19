using Lumina.Models;

namespace Lumina.Services.Flashcards;

public interface IFlashcardService
{
    Task<FlashcardJob> CreateFlashcardJobAsync(Guid documentId, Guid userId);
    Task ProcessFlashcardJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Flashcard>> GetAllAsync(Guid documentId, Guid userId);
    Task<Flashcard?> GetByIdAsync(Guid documentId, Guid flashcardId, Guid userId);
    Task<bool> DeleteAsync(Guid documentId, Guid flashcardId, Guid userId);
}
