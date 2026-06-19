using Lumina.Data;
using Lumina.Extensions;
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
        var owned = await _db.QuizJobs
            .AnyAsync(j => j.Id == jobId && j.Document.UserId == userId);

        if (!owned)
        {
            throw new HubException("You are not authorized to join this job.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, quizJobId);
    }

    public async Task LeaveQuizGroup(string quizJobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, quizJobId);
    }
}
