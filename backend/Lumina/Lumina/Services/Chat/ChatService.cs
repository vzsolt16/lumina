using System.Runtime.CompilerServices;
using System.Text;
using Lumina.Data;
using Lumina.DTOs.Chat;
using Lumina.Models;
using Lumina.Services.AI;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Chat;

public class ChatService : IChatService
{
    // A streamed answer can take minutes, which is far longer than a normal
    // request scope should hold a DbContext open. So instead of injecting a
    // scoped LuminaDbContext, we create a fresh short-lived scope per DB
    // operation — the same convention the background workers use (see CLAUDE.md).
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAiService _aiService;

    public ChatService(IServiceScopeFactory scopeFactory, IAiService aiService)
    {
        _scopeFactory = scopeFactory;
        _aiService = aiService;
    }

    public async Task<List<ChatMessageResponse>> GetHistoryAsync(Guid documentId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

        var owns = await db.Documents
            .AnyAsync(d => d.Id == documentId && d.UserId == userId);

        if (!owns)
        {
            throw new KeyNotFoundException("Document not found.");
        }

        return await db.ChatMessages
            .Where(m => m.DocumentId == documentId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageResponse
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        Guid documentId,
        Guid userId,
        string question,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string fileName;
        string content;
        List<ChatMessage> history;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

            var document = await db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, cancellationToken);

            if (document is null)
            {
                throw new KeyNotFoundException("Document not found.");
            }

            fileName = document.FileName;
            content = document.Content;

            history = await db.ChatMessages
                .Where(m => m.DocumentId == documentId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        var prompt = BuildPrompt(fileName, content, history, question);

        var askedAt = DateTime.UtcNow;
        var answer = new StringBuilder();
        var completed = false;
        var seenContent = false;

        try
        {
            // Headroom for the model's chain of thought plus the answer, so a
            // normal turn isn't cut off mid-stream (qwen3 reasons before replying).
            await foreach (var token in _aiService
                .GenerateStreamAsync(prompt, maxTokens: 4000, cancellationToken: cancellationToken))
            {
                var chunk = token;

                // Skip the leading whitespace the model emits after its (stripped)
                // think block, so the answer doesn't start with blank lines.
                if (!seenContent)
                {
                    chunk = chunk.TrimStart();
                    if (chunk.Length == 0)
                    {
                        continue;
                    }
                    seenContent = true;
                }

                answer.Append(chunk);
                yield return chunk;
            }

            completed = true;
        }
        finally
        {
            // Persist the exchange only when the answer finished cleanly. On an
            // error or a client disconnect mid-stream we save nothing, so history
            // never contains a truncated reply (which would poison later prompts).
            // Both messages are written together — no orphaned user message.
            if (completed)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

                db.ChatMessages.Add(new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    Role = "user",
                    Content = question,
                    CreatedAt = askedAt
                });
                db.ChatMessages.Add(new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    Role = "assistant",
                    Content = answer.ToString(),
                    CreatedAt = DateTime.UtcNow
                });

                // The request token may already be cancelled here; persisting the
                // completed answer shouldn't be abandoned because of that.
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    // The document is injected wholesale for now ("start simple"). When RAG lands,
    // only this method changes: swap the document content for the retrieved chunks.
    private static string BuildPrompt(
        string fileName,
        string content,
        List<ChatMessage> history,
        string question)
    {
        var sb = new StringBuilder();

        sb.AppendLine(
            "You are Lumina, a friendly AI study assistant. The user has opened a document and " +
            "is chatting with you about it.");
        sb.AppendLine(
            "- Greetings, small talk, or questions about you (e.g. \"who are you?\"): reply " +
            "briefly and warmly. You are Lumina, an assistant that helps people understand their " +
            "documents.");
        sb.AppendLine(
            "- Questions about the document's subject: answer from the document below.");
        sb.AppendLine(
            "- If the document doesn't cover something, you may still answer from your own " +
            "general knowledge to stay helpful — just don't make up claims about what this " +
            "specific document says.");
        sb.AppendLine("Keep answers concise and reply directly.");
        sb.AppendLine();
        sb.AppendLine($"--- DOCUMENT: {fileName} ---");
        sb.AppendLine(content);
        sb.AppendLine("--- END DOCUMENT ---");
        sb.AppendLine();

        if (history.Count > 0)
        {
            sb.AppendLine("Conversation so far:");
            foreach (var message in history)
            {
                var speaker = message.Role == "assistant" ? "Assistant" : "User";
                sb.AppendLine($"{speaker}: {message.Content}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"User: {question}");
        sb.Append("Assistant:");

        return sb.ToString();
    }
}
