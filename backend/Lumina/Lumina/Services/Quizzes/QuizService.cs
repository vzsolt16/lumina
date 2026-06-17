using System.Text.Json;
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

        try
        {
            var questions = new List<GeneratedQuizQuestionDto>();
            int totalQuestions = 5;

            for (int i = 1; i <= totalQuestions; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var question = await GenerateSingleQuestionAsync(job.Document.Content, questions, cancellationToken);
                questions.Add(question);

                job.Progress = (int)((double)i / totalQuestions * 100);
                await UpdateJobAndNotifyAsync(job, $"Generated question {i}");
            }

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

    private async Task<GeneratedQuizQuestionDto> GenerateSingleQuestionAsync(string content, List<GeneratedQuizQuestionDto> existingQuestions, CancellationToken cancellationToken)
    {
        var existingContext = existingQuestions.Any() 
            ? "Avoid these questions that were already generated: " + string.Join(" | ", existingQuestions.Select(q => q.Question))
            : "";

        var format = "{ \"question\": \"Question text\", \"correctAnswer\": \"Answer text\" }";
        var prompt = $"""
                      You are generating a study quiz question.
                      Based on the study material below, generate ONE multiple choice or short answer question.
                      
                      {existingContext}

                      Return ONLY valid JSON matching this schema:
                      {format}

                      Study Material:
                      {content}
                      """;

        var response = await _aiService.GenerateAsync(prompt);
        
        try 
        {
            var json = response.Trim();
            if (json.StartsWith("```json")) json = json[7..];
            if (json.StartsWith("```")) json = json[3..];
            if (json.EndsWith("```")) json = json[..^3];
            json = json.Trim();

            return JsonSerializer.Deserialize<GeneratedQuizQuestionDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
                   ?? throw new Exception("Failed to deserialize question.");
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

    public async Task<Quiz> GenerateAsync(Guid documentId)
    {
        throw new NotSupportedException("Use CreateQuizJobAsync instead.");
    }
}