using Microsoft.AspNetCore.SignalR;

namespace Lumina.WebSockets;

public class QuizHub : Hub
{
    public async Task JoinQuizGroup(string quizId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, quizId);
    }

    public async Task LeaveQuizGroup(string quizId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, quizId);
    }
}
