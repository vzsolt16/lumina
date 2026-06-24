using Lumina.DTOs.Chat;

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
    // KeyNotFoundException if the conversation isn't the caller's.
    Task<List<ChatMessageResponse>> GetMessagesAsync(Guid conversationId, Guid userId);

    // Deletes a conversation and (by cascade) its messages. Throws
    // KeyNotFoundException if the conversation isn't the caller's.
    Task DeleteConversationAsync(Guid conversationId, Guid userId);

    // Streams the assistant's answer token-by-token within a conversation. The
    // user's question and the completed answer are both persisted as ChatMessages,
    // and the first question becomes the conversation's title. Throws
    // KeyNotFoundException if the conversation isn't the caller's.
    IAsyncEnumerable<string> StreamAnswerAsync(
        Guid conversationId,
        Guid userId,
        string question,
        CancellationToken cancellationToken = default);
}
