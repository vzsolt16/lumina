using System.Text.Json;
using System.Text.RegularExpressions;
using Lumina.Data;
using Lumina.DTOs.Flashcard;
using Lumina.Models;
using Lumina.Services.AI;
using Lumina.WebSockets;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Flashcards;

public class FlashcardService : IFlashcardService
{
    private static readonly JsonElement FlashcardSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "flashcards": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "question": { "type": "string" },
                            "answer":   { "type": "string" }
                        },
                        "required": ["question", "answer"]
                    }
                }
            },
            "required": ["flashcards"]
        }
        """).RootElement;

    private readonly LuminaDbContext _db;
    private readonly IAiService _aiService;
    private readonly IHubContext<FlashcardHub> _hubContext;
    private readonly ILogger<FlashcardService> _logger;

    public FlashcardService(
        LuminaDbContext db,
        IAiService aiService,
        IHubContext<FlashcardHub> hubContext,
        ILogger<FlashcardService> logger)
    {
        _db = db;
        _aiService = aiService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<FlashcardJob> CreateFlashcardJobAsync(Guid documentId)
    {
        var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId);
        if (!documentExists)
        {
            throw new KeyNotFoundException($"Document {documentId} not found.");
        }

        var job = new FlashcardJob
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Status = JobStatus.Processing,
            Progress = 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.FlashcardJobs.Add(job);
        await _db.SaveChangesAsync();

        return job;
    }

    public async Task ProcessFlashcardJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.FlashcardJobs
            .Include(fj => fj.Document)
            .FirstOrDefaultAsync(fj => fj.Id == jobId, cancellationToken);

        if (job == null)
        {
            _logger.LogError("Flashcard job {JobId} not found.", jobId);
            return;
        }

        using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var timerTask = Task.Run(async () =>
        {
            while (!timerCts.Token.IsCancellationRequested)
            {
                await Task.Delay(1500, timerCts.Token).ContinueWith(_ => { });
                if (timerCts.Token.IsCancellationRequested) break;

                if (job.Progress < 90)
                {
                    job.Progress = Math.Min(job.Progress + 5, 90);
                    await _hubContext.Clients.Group(job.Id.ToString()).SendAsync("Progress", new
                    {
                        flashcardJobId = job.Id,
                        progress = job.Progress,
                        message = "Generating flashcards..."
                    });
                }
            }
        }, timerCts.Token);

        try
        {
            var flashcards = await GenerateAllFlashcardsAsync(job.Document.Content, cancellationToken);

            await timerCts.CancelAsync();
            await timerTask;

            var flashcardEntities = flashcards.Select(f => new Flashcard
            {
                Id = Guid.NewGuid(),
                DocumentId = job.DocumentId,
                Question = f.Question,
                Answer = f.Answer
            }).ToList();

            _db.Flashcards.AddRange(flashcardEntities);

            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.ResultJson = JsonSerializer.Serialize(new GeneratedFlashcardsDto { Flashcards = flashcards });
            job.CompletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            await _hubContext.Clients.Group(job.Id.ToString()).SendAsync("Completed", new
            {
                status = "completed",
                flashcards
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await timerCts.CancelAsync();
            await timerTask;
            _logger.LogError(ex, "Error processing flashcard job {JobId}", jobId);
            job.Status = JobStatus.Failed;
            await _db.SaveChangesAsync(CancellationToken.None);

            await _hubContext.Clients.Group(job.Id.ToString()).SendAsync("Failed", new
            {
                status = "failed",
                error = ex.Message
            }, CancellationToken.None);
        }
    }

    private async Task<List<GeneratedFlashcardDto>> GenerateAllFlashcardsAsync(string content, CancellationToken cancellationToken)
    {
        var prompt = $"""
                      /no_think
                      Generate exactly 10 flashcards from the study material below.
                      Each flashcard must cover a completely different fact or concept — no repeated topics allowed.
                      Each flashcard has one concise question and one clear, direct answer.
                      Draw everything directly from the study material.
                      Study Material:
                      {content}
                      """;

        var response = await _aiService.GenerateAsync(prompt, FlashcardSchema, maxTokens: 2000);

        try
        {
            var text = Regex.Replace(response, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            var json = start >= 0 && end > start ? text[start..(end + 1)] : text;

            var wrapper = JsonSerializer.Deserialize<GeneratedFlashcardsDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? throw new Exception("Failed to deserialize flashcards.");
            return wrapper.Flashcards;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse AI response: {Response}", response);
            throw;
        }
    }

    public async Task<IReadOnlyList<Flashcard>> GetAllAsync(Guid documentId)
    {
        var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId);
        if (!documentExists)
            throw new KeyNotFoundException($"Document {documentId} not found.");

        return await _db.Flashcards
            .Where(f => f.DocumentId == documentId)
            .ToListAsync();
    }

    public async Task<Flashcard?> GetByIdAsync(Guid documentId, Guid flashcardId)
    {
        return await _db.Flashcards
            .FirstOrDefaultAsync(f => f.DocumentId == documentId && f.Id == flashcardId);
    }

    public async Task<bool> DeleteAsync(Guid documentId, Guid flashcardId)
    {
        var flashcard = await _db.Flashcards
            .FirstOrDefaultAsync(f => f.DocumentId == documentId && f.Id == flashcardId);

        if (flashcard == null) return false;

        _db.Flashcards.Remove(flashcard);
        await _db.SaveChangesAsync();
        return true;
    }


}
