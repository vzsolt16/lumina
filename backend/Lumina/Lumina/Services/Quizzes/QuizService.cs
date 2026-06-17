using System.Text.Json;
using System.Text.RegularExpressions;
using Lumina.Data;
using Lumina.DTOs.Quiz;
using Lumina.Models;
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

    public async Task<QuizJob> CreateQuizJobAsync(Guid documentId)
    {
        var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId);
        if (!documentExists)
        {
            throw new KeyNotFoundException($"Document {documentId} not found.");
        }

        var job = new QuizJob
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Status = "Processing",
            Progress = 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.QuizJobs.Add(job);
        await _db.SaveChangesAsync();

        return job;
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

        _ = Task.Run(async () =>
        {
            while (!timerCts.Token.IsCancellationRequested)
            {
                await Task.Delay(1500, timerCts.Token).ContinueWith(_ => { });
                if (timerCts.Token.IsCancellationRequested) break;

                if (job.Progress < 90)
                {
                    job.Progress = Math.Min(job.Progress + 5, 90);
                    await UpdateJobAndNotifyAsync(job, "Generating questions...");
                }
            }
        }, timerCts.Token);

        try
        {
            var questions = await GenerateAllQuestionsAsync(job.Document.Content, cancellationToken);

            await timerCts.CancelAsync();

            job.Progress = 100;
            await UpdateJobAndNotifyAsync(job, "Generated all questions");

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
            
            job.Status = "Completed";
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
            await timerCts.CancelAsync();
            _logger.LogError(ex, "Error processing quiz job {JobId}", jobId);
            job.Status = "Failed";
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

        try
        {
            var text = Regex.Replace(response, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            var json = start >= 0 && end > start ? text[start..(end + 1)] : text;

            var wrapper = JsonSerializer.Deserialize<GeneratedQuizDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? throw new Exception("Failed to deserialize questions.");
            return wrapper.Questions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse AI response: {Response}", response);
            throw;
        }
    }

    private async Task UpdateJobAndNotifyAsync(QuizJob job, string message)
    {
        await _db.SaveChangesAsync();
        await _hubContext.Clients.Group(job.Id.ToString()).SendAsync("Progress", new
        {
            quizId = job.Id,
            progress = job.Progress,
            message = message
        });
    }

    public async Task<IReadOnlyList<Quiz>> GetAllAsync(Guid documentId)
    {
        var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId);
        if (!documentExists)
            throw new KeyNotFoundException($"Document {documentId} not found.");

        return await _db.Quizzes
            .Where(q => q.DocumentId == documentId)
            .Include(q => q.Questions)
            .ToListAsync();
    }

    public async Task<Quiz?> GetByIdAsync(Guid documentId, Guid quizId)
    {
        return await _db.Quizzes
            .Where(q => q.DocumentId == documentId && q.Id == quizId)
            .Include(q => q.Questions)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> DeleteAsync(Guid documentId, Guid quizId)
    {
        var quiz = await _db.Quizzes
            .FirstOrDefaultAsync(q => q.DocumentId == documentId && q.Id == quizId);

        if (quiz == null) return false;

        _db.Quizzes.Remove(quiz);
        await _db.SaveChangesAsync();
        return true;
    }
}