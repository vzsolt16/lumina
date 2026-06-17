using Lumina.Services.Quizzes;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[ApiController]
[Route("api/documents/{documentId:guid}/quizzes")]
public class QuizzesController : ControllerBase
{
    private readonly IQuizService _quizService;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceProvider _serviceProvider;

    public QuizzesController(
        IQuizService quizService,
        IBackgroundTaskQueue taskQueue,
        IServiceProvider serviceProvider)
    {
        _quizService = quizService;
        _taskQueue = taskQueue;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    public async Task<IActionResult> Generate(Guid documentId)
    {
        try
        {
            var job = await _quizService.CreateQuizJobAsync(documentId);

            await _taskQueue.QueueBackgroundWorkItemAsync(async token =>
            {
                using var scope = _serviceProvider.CreateScope();
                var scopedQuizService = scope.ServiceProvider.GetRequiredService<IQuizService>();
                await scopedQuizService.ProcessQuizJobAsync(job.Id, token);
            });

            return Ok(new { quizId = job.Id, status = job.Status });
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
            var quizzes = await _quizService.GetAllAsync(documentId);
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
        var quiz = await _quizService.GetByIdAsync(documentId, quizId);
        if (quiz == null) return NotFound();
        return Ok(quiz);
    }

    [HttpDelete("{quizId:guid}")]
    public async Task<IActionResult> Delete(Guid documentId, Guid quizId)
    {
        var deleted = await _quizService.DeleteAsync(documentId, quizId);
        if (!deleted) return NotFound();
        return NoContent();
    }
}