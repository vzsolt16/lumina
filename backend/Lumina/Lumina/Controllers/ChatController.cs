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
            var messages = await _chatService.GetMessagesAsync(documentId, conversationId, User.GetUserId());
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
            await _chatService.DeleteConversationAsync(documentId, conversationId, User.GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // Applies an AI edit proposal to the document and returns the updated
    // document, so the client can refresh its copy of the content.
    [HttpPost("{conversationId:guid}/messages/{messageId:guid}/proposal/apply")]
    public async Task<IActionResult> ApplyProposal(Guid documentId, Guid conversationId, Guid messageId)
    {
        try
        {
            var document = await _chatService.ApplyProposalAsync(documentId, conversationId, messageId, User.GetUserId());
            return Ok(document);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            // Already resolved, or the document changed under the proposal.
            return Conflict(ex.Message);
        }
    }

    [HttpPost("{conversationId:guid}/messages/{messageId:guid}/proposal/reject")]
    public async Task<IActionResult> RejectProposal(Guid documentId, Guid conversationId, Guid messageId)
    {
        try
        {
            await _chatService.RejectProposalAsync(documentId, conversationId, messageId, User.GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }
}
