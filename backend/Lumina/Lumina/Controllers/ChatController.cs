using Lumina.Extensions;
using Lumina.Services.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[Authorize]
[ApiController]
[Route("api/documents/{documentId:guid}/chat/conversations")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    // Lists the document's conversations so the UI can render the sidebar.
    [HttpGet]
    public async Task<IActionResult> GetConversations(Guid documentId)
    {
        try
        {
            var conversations = await _chatService.GetConversationsAsync(documentId, User.GetUserId());
            return Ok(conversations);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // Starts a new, empty conversation. Streaming answers into it happens over
    // SignalR (ChatHub at /ws/chat), not here.
    [HttpPost]
    public async Task<IActionResult> CreateConversation(Guid documentId)
    {
        try
        {
            var conversation = await _chatService.CreateConversationAsync(documentId, User.GetUserId());
            return CreatedAtAction(
                nameof(GetMessages),
                new { documentId, conversationId = conversation.Id },
                conversation);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // Loads one conversation's messages so the UI can render it when reopened.
    [HttpGet("{conversationId:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid documentId, Guid conversationId)
    {
        try
        {
            var messages = await _chatService.GetMessagesAsync(conversationId, User.GetUserId());
            return Ok(messages);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{conversationId:guid}")]
    public async Task<IActionResult> DeleteConversation(Guid documentId, Guid conversationId)
    {
        try
        {
            await _chatService.DeleteConversationAsync(conversationId, User.GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
