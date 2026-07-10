using Lumina.DTOs.Chat;
using Lumina.DTOs.Document;

namespace Lumina.Services.Chat;

public interface IChatService
{
    // Lists a document's conversations, most-recently-updated first. Throws
    // KeyNotFoundException if the document doesn't exist or isn't the caller's.
    Task<List<ChatConversationResponse>> GetConversationsAsync(Guid documentId, Guid userId);

    // Creates a new, empty conversation for a document. Throws
    // KeyNotFoundException if the document isn't the caller's.
    Task<ChatConversationResponse> CreateConversationAsync(Guid documentId, Guid userId);

    // Returns one conversation's messages, oldest first. Throws
    // KeyNotFoundException if the conversation isn't the caller's or doesn't
    // belong to the given document.
    Task<List<ChatMessageResponse>> GetMessagesAsync(Guid documentId, Guid conversationId, Guid userId);

    // Deletes a conversation and (by cascade) its messages. Throws
    // KeyNotFoundException if the conversation isn't the caller's or doesn't
    // belong to the given document.
    Task DeleteConversationAsync(Guid documentId, Guid conversationId, Guid userId);

    // Streams the assistant's answer as typed events: "token" fragments of the
    // visible reply, plus at most one trailing "proposal" event when the model
    // proposed a document edit. The user's question and the completed answer
    // (with any proposal) are persisted as ChatMessages, and the first question
    // becomes the conversation's title. Throws KeyNotFoundException if the
    // conversation isn't the caller's.
    IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
        Guid conversationId,
        Guid userId,
        string question,
        CancellationToken cancellationToken = default);

    // Applies a pending edit proposal to its document and returns the updated
    // document. Throws KeyNotFoundException if the proposal isn't the caller's
    // (or the document/conversation/message chain doesn't line up),
    // InvalidOperationException if it's already resolved or no longer applies.
    Task<DocumentDetailResponse> ApplyProposalAsync(
        Guid documentId, Guid conversationId, Guid messageId, Guid userId);

    // Marks a pending edit proposal as rejected without touching the document.
    // Same exceptions as ApplyProposalAsync.
    Task RejectProposalAsync(Guid documentId, Guid conversationId, Guid messageId, Guid userId);
}
