using Lumina.DTOs.Chat;

namespace Lumina.Services.Chat;

public interface IChatService
{
    // Returns the full conversation for a document, oldest first. Throws
    // KeyNotFoundException if the document doesn't exist or isn't the caller's.
    Task<List<ChatMessageResponse>> GetHistoryAsync(Guid documentId, Guid userId);

    // Streams the assistant's answer token-by-token. The user's question and the
    // completed answer are both persisted as ChatMessages. Throws
    // KeyNotFoundException if the document isn't the caller's.
    IAsyncEnumerable<string> StreamAnswerAsync(
        Guid documentId,
        Guid userId,
        string question,
        CancellationToken cancellationToken = default);
}
