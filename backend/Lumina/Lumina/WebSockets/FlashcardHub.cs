using Lumina.Data;
using Lumina.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lumina.WebSockets;

[Authorize]
public class FlashcardHub : Hub
{
    private readonly LuminaDbContext _db;

    public FlashcardHub(LuminaDbContext db)
    {
        _db = db;
    }

    // Only let a caller subscribe to a job that belongs to one of their own
    // documents. The job id is a GUID, but we don't rely on it being unguessable.
    public async Task JoinFlashcardGroup(string flashcardJobId)
    {
        if (!Guid.TryParse(flashcardJobId, out var jobId))
        {
            throw new HubException("Invalid job id.");
        }

        var userId = Context.User!.GetUserId();
        var owned = await _db.FlashcardJobs
            .AnyAsync(j => j.Id == jobId && j.Document.UserId == userId);

        if (!owned)
        {
            throw new HubException("You are not authorized to join this job.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, flashcardJobId);
    }
}
