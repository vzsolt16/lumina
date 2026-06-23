using Lumina.Extensions;
using Lumina.Services.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[Authorize]
[ApiController]
[Route("api/documents/{documentId:guid}/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    // Loads the existing conversation so the UI can render it on open. Streaming
    // new answers happens over SignalR (ChatHub at /ws/chat), not here.
    [HttpGet]
    public async Task<IActionResult> GetHistory(Guid documentId)
    {
        try
        {
            var messages = await _chatService.GetHistoryAsync(documentId, User.GetUserId());
            return Ok(messages);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
