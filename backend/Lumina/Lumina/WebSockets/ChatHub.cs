using System.Runtime.CompilerServices;
using Lumina.Data;
using Lumina.Extensions;
using Lumina.Services.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lumina.WebSockets;

[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly LuminaDbContext _db;

    public ChatHub(IChatService chatService, LuminaDbContext db)
    {
        _chatService = chatService;
        _db = db;
    }

    // Server-to-client streaming: the client invokes this with `connection.stream`
    // and receives the answer token-by-token. SignalR supplies the
    // CancellationToken and cancels it if the client unsubscribes or disconnects.
    public async IAsyncEnumerable<string> StreamAnswer(
        Guid conversationId,
        string question,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new HubException("Question must not be empty.");
        }

        // Guard against a pathologically long question bloating the prompt. The
        // document itself is bounded at upload (2 MB); this caps the user input.
        if (question.Length > 4000)
        {
            throw new HubException("Question is too long.");
        }

        var userId = Context.User!.GetUserId();

        // Validate ownership before streaming so we can return a clean HubException
        // (the service re-checks too, but throws KeyNotFoundException internally).
        var owns = await _db.ChatConversations
            .AnyAsync(c => c.Id == conversationId && c.Document.UserId == userId, cancellationToken);

        if (!owns)
        {
            throw new HubException("Conversation not found.");
        }

        await foreach (var token in _chatService
            .StreamAnswerAsync(conversationId, userId, question, cancellationToken))
        {
            yield return token;
        }
    }
}
