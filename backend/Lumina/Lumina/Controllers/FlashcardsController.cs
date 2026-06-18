using Lumina.Services.Flashcards;
using Lumina.Services.Quizzes;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[ApiController]
[Route("api/documents/{documentId:guid}/flashcards")]
public class FlashcardsController : ControllerBase
{
    private readonly IFlashcardService _flashcardService;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceScopeFactory _scopeFactory;

    public FlashcardsController(
        IFlashcardService flashcardService,
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory)
    {
        _flashcardService = flashcardService;
        _taskQueue = taskQueue;
        _scopeFactory = scopeFactory;
    }

    [HttpPost]
    public async Task<IActionResult> Generate(Guid documentId)
    {
        try
        {
            var job = await _flashcardService.CreateFlashcardJobAsync(documentId);

            await _taskQueue.QueueBackgroundWorkItemAsync(async token =>
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedFlashcardService = scope.ServiceProvider.GetRequiredService<IFlashcardService>();
                await scopedFlashcardService.ProcessFlashcardJobAsync(job.Id, token);
            });

            return Ok(new { flashcardJobId = job.Id, status = job.Status });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid documentId)
    {
        try
        {
            var flashcards = await _flashcardService.GetAllAsync(documentId);
            return Ok(flashcards);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("{flashcardId:guid}")]
    public async Task<IActionResult> GetById(Guid documentId, Guid flashcardId)
    {
        var flashcard = await _flashcardService.GetByIdAsync(documentId, flashcardId);
        if (flashcard == null) return NotFound();
        return Ok(flashcard);
    }

    [HttpDelete("{flashcardId:guid}")]
    public async Task<IActionResult> Delete(Guid documentId, Guid flashcardId)
    {
        var deleted = await _flashcardService.DeleteAsync(documentId, flashcardId);
        if (!deleted) return NotFound();
        return NoContent();
    }
}
