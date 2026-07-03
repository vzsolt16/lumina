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

    public async Task<List<ChatConversationResponse>> GetConversationsAsync(Guid documentId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

        var owns = await db.Documents
            .AnyAsync(d => d.Id == documentId && d.UserId == userId);

        if (!owns)
        {
            throw new KeyNotFoundException("Document not found.");
        }

        return await db.ChatConversations
            .Where(c => c.DocumentId == documentId)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ChatConversationResponse
            {
                Id = c.Id,
                Title = c.Title,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task<ChatConversationResponse> CreateConversationAsync(Guid documentId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

        var owns = await db.Documents
            .AnyAsync(d => d.Id == documentId && d.UserId == userId);

        if (!owns)
        {
            throw new KeyNotFoundException("Document not found.");
        }

        var now = DateTime.UtcNow;
        var conversation = new ChatConversation
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Title = "New chat",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ChatConversations.Add(conversation);
        await db.SaveChangesAsync();

        return new ChatConversationResponse
        {
            Id = conversation.Id,
            Title = conversation.Title,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt
        };
    }

    public async Task<List<ChatMessageResponse>> GetMessagesAsync(Guid conversationId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

        var owns = await db.ChatConversations
            .AnyAsync(c => c.Id == conversationId && c.Document.UserId == userId);

        if (!owns)
        {
            throw new KeyNotFoundException("Conversation not found.");
        }

        return await db.ChatMessages
            .Where(m => m.ConversationId == conversationId)
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

    public async Task DeleteConversationAsync(Guid conversationId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

        var conversation = await db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.Document.UserId == userId);

        if (conversation is null)
        {
            throw new KeyNotFoundException("Conversation not found.");
        }

        // Messages are removed by the cascade configured on ChatConversation → Messages.
        db.ChatConversations.Remove(conversation);
        await db.SaveChangesAsync();
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        Guid conversationId,
        Guid userId,
        string question,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string fileName;
        string content;
        bool isFirstMessage;
        List<ChatMessage> history;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

            var conversation = await db.ChatConversations
                .Include(c => c.Document)
                .FirstOrDefaultAsync(
                    c => c.Id == conversationId && c.Document.UserId == userId,
                    cancellationToken);

            if (conversation is null)
            {
                throw new KeyNotFoundException("Conversation not found.");
            }

            fileName = conversation.Document.FileName;
            content = conversation.Document.Content;

            history = await db.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(cancellationToken);

            isFirstMessage = history.Count == 0;
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

                var completedAt = DateTime.UtcNow;

                db.ChatMessages.Add(new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    Role = "user",
                    Content = question,
                    CreatedAt = askedAt
                });
                db.ChatMessages.Add(new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    Role = "assistant",
                    Content = answer.ToString(),
                    CreatedAt = completedAt
                });

                // Bump the conversation so it sorts to the top, and name it from
                // the opening question the first time round.
                var conversation = await db.ChatConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId, CancellationToken.None);
                if (conversation is not null)
                {
                    conversation.UpdatedAt = completedAt;
                    if (isFirstMessage)
                    {
                        conversation.Title = DeriveTitle(question);
                    }
                }

                // The request token may already be cancelled here; persisting the
                // completed answer shouldn't be abandoned because of that.
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    // The list shows a short label per conversation; take the first line of the
    // opening question, trimmed to a sane length.
    private static string DeriveTitle(string question)
    {
        var firstLine = question
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        const int maxLength = 40;
        if (firstLine.Length <= maxLength)
        {
            return firstLine.Length == 0 ? "New chat" : firstLine;
        }

        return firstLine[..maxLength].TrimEnd() + "…";
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
            "NEVER use standard emojis (e.g., 😊, ✅)." + 
            "You may ONLY use kaomojis or ASCII art (e.g., (ɔ◔‿◔)ɔ, ¯(ツ)/¯, ♥) to add warmth or personality " +
            "— but only if they are in the style of traditional kaomojis and not standard emojis.");
        sb.AppendLine(
            "- Questions about the document's subject: answer from the document below.");
        sb.AppendLine(
            "- If the document doesn't cover something, you may still answer from your own " +
            "general knowledge to stay helpful — just don't make up claims about what this " +
            "specific document says.");
        sb.AppendLine("Keep answers concise and reply directly.");
        sb.AppendLine(
            "Format your replies in Markdown so they're easy to read: use **bold** for key " +
            "terms, bullet or numbered lists for steps and enumerations, `inline code` and " +
            "fenced code blocks for code or commands, tables for structured comparisons, and " +
            "short headings to break up longer answers. Don't overformat short or conversational " +
            "replies — a plain sentence is fine for greetings and small talk.");
        sb.AppendLine(
            "Use pure Markdown only — never write raw HTML tags (no <ul>, <li>, <br>, etc.); " +
            "they will not render. Markdown table cells cannot contain bullet lists, so for any " +
            "data with per-row lists, use a normal bullet list with bold labels instead of a " +
            "table, or keep each cell to short comma-separated text.");
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
