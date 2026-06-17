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
    private readonly IServiceProvider _serviceProvider;

    public FlashcardsController(
        IFlashcardService flashcardService,
        IBackgroundTaskQueue taskQueue,
        IServiceProvider serviceProvider)
    {
        _flashcardService = flashcardService;
        _taskQueue = taskQueue;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    public async Task<IActionResult> Generate(Guid documentId)
    {
        try
        {
            var job = await _flashcardService.CreateFlashcardJobAsync(documentId);

            await _taskQueue.QueueBackgroundWorkItemAsync(async token =>
            {
                using var scope = _serviceProvider.CreateScope();
                var scopedFlashcardService = scope.ServiceProvider.GetRequiredService<IFlashcardService>();
                await scopedFlashcardService.ProcessFlashcardJobAsync(job.Id, token);
            });

            return Ok(new
            {
                flashcardJobId = job.Id,
                status = job.Status
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
