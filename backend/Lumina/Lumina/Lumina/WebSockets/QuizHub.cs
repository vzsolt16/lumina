using System.Text.Json;
using Lumina.Data;
using Lumina.DTOs.Quiz;
using Lumina.Extensions;
using Lumina.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lumina.WebSockets;

[Authorize]
public class QuizHub : Hub
{
    private readonly LuminaDbContext _db;

    public QuizHub(LuminaDbContext db)
    {
        _db = db;
    }

    // Only let a caller subscribe to a job that belongs to one of their own
    // documents. The job id is a GUID, but we don't rely on it being unguessable.
    public async Task JoinQuizGroup(string quizJobId)
    {
        if (!Guid.TryParse(quizJobId, out var jobId))
        {
            throw new HubException("Invalid job id.");
        }

        var userId = Context.User!.GetUserId();
        var job = await _db.QuizJobs
            .FirstOrDefaultAsync(j => j.Id == jobId && j.Document.UserId == userId);

        if (job is null)
        {
            throw new HubException("You are not authorized to join this job.");
        }

        // Join first so we never miss a terminal event fired while we're joining.
        await Groups.AddToGroupAsync(Context.ConnectionId, quizJobId);

        // If the job already finished before the client joined, the terminal
        // event was broadcast to an empty group and lost. Replay it to this
        // connection so the client doesn't wait forever. (A duplicate is
        // harmless and far better than a missed completion.)
        if (job.Status == JobStatus.Completed && job.ResultJson is not null)
        {
            var quiz = JsonSerializer.Deserialize<GeneratedQuizDto>(job.ResultJson);
            await Clients.Caller.SendAsync("Completed", new { status = "completed", quiz });
        }
        else if (job.Status == JobStatus.Failed)
        {
            await Clients.Caller.SendAsync("Failed", new { status = "failed", error = "Generation failed." });
        }
    }

    public async Task LeaveQuizGroup(string quizJobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, quizJobId);
    }
}
