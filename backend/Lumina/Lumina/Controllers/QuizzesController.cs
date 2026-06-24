using Lumina.Extensions;
using Lumina.Services.Quizzes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[Authorize]
[ApiController]
[Route("api/documents/{documentId:guid}/quizzes")]
public class QuizzesController : ControllerBase
{
    private readonly IQuizService _quizService;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceScopeFactory _scopeFactory;

    public QuizzesController(
        IQuizService quizService,
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory)
    {
        _quizService = quizService;
        _taskQueue = taskQueue;
        _scopeFactory = scopeFactory;
    }

    [HttpPost]
    public async Task<IActionResult> Generate(Guid documentId)
    {
        try
        {
            var job = await _quizService.CreateQuizJobAsync(documentId, User.GetUserId());

            var enqueued = _taskQueue.TryEnqueue(async token =>
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedQuizService = scope.ServiceProvider.GetRequiredService<IQuizService>();
                await scopedQuizService.ProcessQuizJobAsync(job.Id, token);
            });

            if (!enqueued)
            {
                // Queue is full: shed load instead of blocking the request thread.
                await _quizService.MarkJobFailedAsync(job.Id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = "The server is busy. Please try again shortly." });
            }

            return Accepted(new { quizId = job.Id, status = job.Status });
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
            var quizzes = await _quizService.GetAllAsync(documentId, User.GetUserId());
            return Ok(quizzes);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("{quizId:guid}")]
    public async Task<IActionResult> GetById(Guid documentId, Guid quizId)
    {
        var quiz = await _quizService.GetByIdAsync(documentId, quizId, User.GetUserId());
        if (quiz == null) return NotFound();
        return Ok(quiz);
    }

    [HttpDelete("{quizId:guid}")]
    public async Task<IActionResult> Delete(Guid documentId, Guid quizId)
    {
        var deleted = await _quizService.DeleteAsync(documentId, quizId, User.GetUserId());
        if (!deleted) return NotFound();
        return NoContent();
    }
}