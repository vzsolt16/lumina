using Microsoft.AspNetCore.SignalR;

namespace Lumina.WebSockets;

public class FlashcardHub : Hub
{
    public async Task JoinFlashcardGroup(string flashcardJobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, flashcardJobId);
    }
}
