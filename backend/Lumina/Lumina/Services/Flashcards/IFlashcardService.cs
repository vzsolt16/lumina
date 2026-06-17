using Lumina.Models;

namespace Lumina.Services.Flashcards;

public interface IFlashcardService
{
    Task<FlashcardJob> CreateFlashcardJobAsync(Guid documentId);
    Task ProcessFlashcardJobAsync(Guid jobId, CancellationToken cancellationToken);
}
