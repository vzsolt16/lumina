using Lumina.Models;

namespace Lumina.Services.Flashcards;

public interface IFlashcardService
{
    Task<FlashcardJob> CreateFlashcardJobAsync(Guid documentId);
    Task ProcessFlashcardJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Flashcard>> GetAllAsync(Guid documentId);
    Task<Flashcard?> GetByIdAsync(Guid documentId, Guid flashcardId);
    Task<bool> DeleteAsync(Guid documentId, Guid flashcardId);
}
