using System.Text.Json;
using Lumina.Data;
using Lumina.DTOs.Quiz;
using Lumina.Models;
using Lumina.Services.Ai;
using Lumina.Services.AI;
using Lumina.WebSockets;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Quizzes;

public class QuizService : IQuizService
{
    private static readonly JsonElement QuizSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "questions": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "question":      { "type": "string" },
                            "answerA":       { "type": "string" },
                            "answerB":       { "type": "string" },
                            "answerC":       { "type": "string" },
                            "answerD":       { "type": "string" },
                            "correctAnswer": { "type": "string", "enum": ["A", "B", "C", "D"] }
                        },
                        "required": ["question", "answerA", "answerB", "answerC", "answerD", "correctAnswer"]
                    }
                }
            },
            "required": ["questions"]
        }
        """).RootElement;
    private readonly LuminaDbContext _db;
    private readonly IAiService _aiService;
    private readonly IHubContext<QuizHub> _hubContext;
    private readonly ILogger<QuizService> _logger;

    public QuizService(
        LuminaDbContext db,
        IAiService aiService,
        IHubContext<QuizHub> hubContext,
        ILogger<QuizService> logger)
    {
        _db = db;
        _aiService = aiService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<QuizJob> CreateQuizJobAsync(Guid documentId, Guid userId)
    {
        var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId && d.UserId == userId);
        if (!documentExists)
        {
            throw new KeyNotFoundException($"Document {documentId} not found.");
        }

        var job = new QuizJob
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Status = JobStatus.Processing,
            Progress = 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.QuizJobs.Add(job);
        await _db.SaveChangesAsync();

        return job;
    }

    public async Task MarkJobFailedAsync(Guid jobId)
    {
        var job = await _db.QuizJobs.FirstOrDefaultAsync(qj => qj.Id == jobId);
        if (job is null) return;

        job.Status = JobStatus.Failed;
        await _db.SaveChangesAsync();
    }

    public async Task ProcessQuizJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.QuizJobs
            .Include(qj => qj.Document)
            .FirstOrDefaultAsync(qj => qj.Id == jobId, cancellationToken);

        if (job == null)
        {
            _logger.LogError("Quiz job {JobId} not found.", jobId);
            return;
        }

        using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Progress lives in a local the timer thread alone owns — never the
        // EF-tracked 'job' entity — so the timer and the main flow never touch
        // the DbContext concurrently. job.Progress is set once, on the main
        // thread, right before SaveChanges.
        var progress = 0;
        var timerTask = Task.Run(async () =>
        {
            while (!timerCts.Token.IsCancellationRequested)
            {
                await Task.Delay(1500, timerCts.Token).ContinueWith(_ => { });
                if (timerCts.Token.IsCancellationRequested) break;

                if (progress < 90)
                {
                    progress = Math.Min(progress + 5, 90);
                    await _hubContext.Clients.Group(jobId.ToString()).SendAsync("Progress", new
                    {
                        quizId = jobId,
                        progress,
                        message = "Generating questions..."
                    });
                }
            }
        }, timerCts.Token);

        // Stop the progress timer, swallowing any fault from a tick's SendAsync
        // so a faulted timer can never stop the job from being finalized/notified.
        async Task StopTimerAsync()
        {
            try
            {
                await timerCts.CancelAsync();
                await timerTask;
            }
            catch (Exception timerEx)
            {
                _logger.LogWarning(timerEx, "Progress timer for quiz job {JobId} faulted while stopping.", jobId);
            }
        }

        try
        {
            var questions = await GenerateAllQuestionsAsync(job.Document.Content, cancellationToken);

            await StopTimerAsync();

            var finalQuizDto = new GeneratedQuizDto
            {
                Title = $"Quiz for {job.Document.FileName}",
                Questions = questions
            };

            var quiz = new Quiz
            {
                Id = Guid.NewGuid(),
                DocumentId = job.DocumentId,
                Title = finalQuizDto.Title,
                Questions = questions.Select(q => new QuizQuestion
                {
                    Id = Guid.NewGuid(),
                    Question = q.Question,
                    AnswerA = q.AnswerA,
                    AnswerB = q.AnswerB,
                    AnswerC = q.AnswerC,
                    AnswerD = q.AnswerD,
                    CorrectAnswer = q.CorrectAnswer
                }).ToList()
            };

            _db.Quizzes.Add(quiz);
            
            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.ResultJson = JsonSerializer.Serialize(finalQuizDto);
            job.CompletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await _hubContext.Clients.Group(job.Id.ToString()).SendAsync("Completed", new
            {
                status = "completed",
                quiz = finalQuizDto
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await StopTimerAsync();
            _logger.LogError(ex, "Error processing quiz job {JobId}", jobId);
            job.Status = JobStatus.Failed;
            await _db.SaveChangesAsync(CancellationToken.None);

            await _hubContext.Clients.Group(job.Id.ToString()).SendAsync("Failed", new
            {
                status = "failed",
                error = ex.Message
            }, CancellationToken.None);
        }
    }

    private async Task<List<GeneratedQuizQuestionDto>> GenerateAllQuestionsAsync(string content, CancellationToken cancellationToken)
    {
        var prompt = $"""
                      /no_think
                      Generate exactly 5 multiple-choice quiz questions from the study material below.
                      Each question MUST cover a completely different fact or topic — no repeated topics allowed.
                      Each question needs exactly 4 answer options (A, B, C, D).
                      Only one answer is correct. The other three must be plausible but wrong.
                      Set correctAnswer to the letter ("A", "B", "C", or "D") of the correct option.
                      Draw everything directly from the study material.
                      Study Material:
                      {content}
                      """;

        var response = await _aiService.GenerateAsync(prompt, QuizSchema, maxTokens: 1200);

        return AiJsonParser.Parse<GeneratedQuizDto>(response, _logger).Questions;
    }

    public async Task<IReadOnlyList<QuizResponse>> GetAllAsync(Guid documentId, Guid userId)
    {
        var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId && d.UserId == userId);
        if (!documentExists)
            throw new KeyNotFoundException($"Document {documentId} not found.");

        return await _db.Quizzes
            .Where(q => q.DocumentId == documentId)
            .Select(ToResponse)
            .ToListAsync();
    }

    public async Task<QuizResponse?> GetByIdAsync(Guid documentId, Guid quizId, Guid userId)
    {
        return await _db.Quizzes
            .Where(q => q.DocumentId == documentId && q.Id == quizId && q.Document.UserId == userId)
            .Select(ToResponse)
            .FirstOrDefaultAsync();
    }

    // Projects the quiz graph to a DTO. Returning the entity directly would
    // cycle during JSON serialization (Quiz -> Questions -> Quiz).
    private static readonly System.Linq.Expressions.Expression<Func<Quiz, QuizResponse>> ToResponse =
        q => new QuizResponse
        {
            Id = q.Id,
            Title = q.Title,
            Questions = q.Questions.Select(qq => new QuizQuestionResponse
            {
                Id = qq.Id,
                Question = qq.Question,
                AnswerA = qq.AnswerA,
                AnswerB = qq.AnswerB,
                AnswerC = qq.AnswerC,
                AnswerD = qq.AnswerD,
                CorrectAnswer = qq.CorrectAnswer,
            }).ToList()
        };

    public async Task<bool> DeleteAsync(Guid documentId, Guid quizId, Guid userId)
    {
        var quiz = await _db.Quizzes
            .FirstOrDefaultAsync(q => q.DocumentId == documentId && q.Id == quizId && q.Document.UserId == userId);

        if (quiz == null) return false;

        _db.Quizzes.Remove(quiz);
        await _db.SaveChangesAsync();
        return true;
    }
}