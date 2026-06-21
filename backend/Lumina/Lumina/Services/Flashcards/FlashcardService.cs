using System.Text.Json;
using Lumina.Data;
using Lumina.DTOs.Flashcard;
using Lumina.Models;
using Lumina.Services.Ai;
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

    public async Task<FlashcardJob> CreateFlashcardJobAsync(Guid documentId, Guid userId)
    {
        var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId && d.UserId == userId);
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
                _logger.LogWarning(timerEx, "Progress timer for flashcard job {JobId} faulted while stopping.", jobId);
            }
        }

        try
        {
            var flashcards = await GenerateAllFlashcardsAsync(job.Document.Content, cancellationToken);

            await StopTimerAsync();

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
            await StopTimerAsync();
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

        return AiJsonParser.Parse<GeneratedFlashcardsDto>(response, _logger).Flashcards;
    }

    public async Task<IReadOnlyList<FlashcardResponse>> GetAllAsync(Guid documentId, Guid userId)
    {
        var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId && d.UserId == userId);
        if (!documentExists)
            throw new KeyNotFoundException($"Document {documentId} not found.");

        return await _db.Flashcards
            .Where(f => f.DocumentId == documentId)
            .Select(ToResponse)
            .ToListAsync();
    }

    public async Task<FlashcardResponse?> GetByIdAsync(Guid documentId, Guid flashcardId, Guid userId)
    {
        return await _db.Flashcards
            .Where(f => f.DocumentId == documentId && f.Id == flashcardId && f.Document.UserId == userId)
            .Select(ToResponse)
            .FirstOrDefaultAsync();
    }

    // Projects to a DTO so we don't serialize the entity (and its Document
    // navigation) out to the client.
    private static readonly System.Linq.Expressions.Expression<Func<Flashcard, FlashcardResponse>> ToResponse =
        f => new FlashcardResponse
        {
            Id = f.Id,
            Question = f.Question,
            Answer = f.Answer,
        };

    public async Task<bool> DeleteAsync(Guid documentId, Guid flashcardId, Guid userId)
    {
        var flashcard = await _db.Flashcards
            .FirstOrDefaultAsync(f => f.DocumentId == documentId && f.Id == flashcardId && f.Document.UserId == userId);

        if (flashcard == null) return false;

        _db.Flashcards.Remove(flashcard);
        await _db.SaveChangesAsync();
        return true;
    }


}
