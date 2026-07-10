using System.Runtime.CompilerServices;
using System.Text;
using Lumina.Data;
using Lumina.DTOs.Chat;
using Lumina.DTOs.Document;
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

    public async Task<List<ChatMessageResponse>> GetMessagesAsync(Guid documentId, Guid conversationId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

        var owns = await db.ChatConversations
            .AnyAsync(c => c.Id == conversationId
                && c.DocumentId == documentId
                && c.Document.UserId == userId);

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
                CreatedAt = m.CreatedAt,
                Proposal = m.ProposalTarget == null ? null : new EditProposalResponse
                {
                    MessageId = m.Id,
                    Target = m.ProposalTarget,
                    Replacement = m.ProposalReplacement ?? string.Empty,
                    Status = m.ProposalStatus ?? EditProposalStatus.Pending
                }
            })
            .ToListAsync();
    }

    public async Task DeleteConversationAsync(Guid documentId, Guid conversationId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

        var conversation = await db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId
                && c.DocumentId == documentId
                && c.Document.UserId == userId);

        if (conversation is null)
        {
            throw new KeyNotFoundException("Conversation not found.");
        }

        // Messages are removed by the cascade configured on ChatConversation → Messages.
        db.ChatConversations.Remove(conversation);
        await db.SaveChangesAsync();
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamAnswerAsync(
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
        // Generated up front so the proposal event streamed to the client refers
        // to the same message id the proposal is persisted under.
        var assistantMessageId = Guid.NewGuid();

        // What the user actually sees — the raw stream minus the edit-proposal
        // marker block. This (not the raw answer) is persisted as the message.
        var visible = new StringBuilder();
        var filter = new EditProposalStreamFilter();
        string? proposalTarget = null;
        string? proposalReplacement = null;
        var completed = false;
        var seenContent = false;

        try
        {
            // Headroom for the model's chain of thought plus the answer, so a
            // normal turn isn't cut off mid-stream (qwen3 reasons before replying).
            await foreach (var token in _aiService
                .GenerateStreamAsync(prompt, maxTokens: 4000, cancellationToken: cancellationToken))
            {
                var chunk = filter.Push(token);
                if (chunk.Length == 0)
                {
                    continue;
                }

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

                visible.Append(chunk);
                yield return ChatStreamEvent.ForToken(chunk);
            }

            var (remainder, proposal) = filter.Finish();

            if (remainder.Length > 0)
            {
                if (!seenContent)
                {
                    remainder = remainder.TrimStart();
                }
                if (remainder.Length > 0)
                {
                    seenContent = true;
                    visible.Append(remainder);
                    yield return ChatStreamEvent.ForToken(remainder);
                }
            }

            if (proposal is not null)
            {
                var (target, replacement) = proposal.Value;

                // The edit is only applicable if the model quoted the document
                // verbatim and unambiguously (exactly one occurrence — otherwise
                // apply could rewrite the wrong passage); if not, say so instead
                // of showing a broken diff.
                var occurrences = CountOccurrences(content, target);

                if (target.Length > 0 && target != replacement && occurrences == 1)
                {
                    proposalTarget = target;
                    proposalReplacement = replacement;

                    yield return ChatStreamEvent.ForProposal(new EditProposalResponse
                    {
                        MessageId = assistantMessageId,
                        Target = target,
                        Replacement = replacement,
                        Status = EditProposalStatus.Pending
                    });
                }
                else
                {
                    var reason = occurrences > 1
                        ? "the passage I tried to change appears more than once in the "
                          + "document, so applying it could change the wrong one"
                        : "the passage I tried to change doesn't match the document exactly";
                    var note = (visible.Length > 0 ? "\n\n" : string.Empty)
                        + $"_I drafted an edit, but {reason}, so I can't offer it safely. "
                        + "Try asking again, quoting the part you want changed._";
                    visible.Append(note);
                    yield return ChatStreamEvent.ForToken(note);
                }
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
                    Id = assistantMessageId,
                    ConversationId = conversationId,
                    Role = "assistant",
                    Content = visible.ToString(),
                    CreatedAt = completedAt,
                    ProposalTarget = proposalTarget,
                    ProposalReplacement = proposalReplacement,
                    ProposalStatus = proposalTarget is null ? null : EditProposalStatus.Pending
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

    public async Task<DocumentDetailResponse> ApplyProposalAsync(
        Guid documentId, Guid conversationId, Guid messageId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

        var message = await LoadProposalMessageAsync(db, documentId, conversationId, messageId, userId);
        var document = message.Conversation.Document;

        // The document may have been edited since the proposal was made; re-check
        // that the passage still exists — and is still unambiguous — rather than
        // blindly rewriting content.
        var occurrences = CountOccurrences(document.Content, message.ProposalTarget!);
        if (occurrences == 0)
        {
            throw new InvalidOperationException(
                "The document has changed since this edit was proposed; the passage to replace no longer exists.");
        }
        if (occurrences > 1)
        {
            throw new InvalidOperationException(
                "The document has changed since this edit was proposed; the passage to replace now appears more than once.");
        }

        var index = document.Content.IndexOf(message.ProposalTarget!, StringComparison.Ordinal);

        var newContent = document.Content
            .Remove(index, message.ProposalTarget!.Length)
            .Insert(index, message.ProposalReplacement ?? string.Empty);

        const long MaxContentSize = 2 * 1024 * 1024; // mirror the upload/edit cap
        var size = Encoding.UTF8.GetByteCount(newContent);
        if (size > MaxContentSize)
        {
            throw new InvalidOperationException("Applying this edit would exceed the 2 MB document limit.");
        }

        document.Content = newContent;
        document.FileSize = size;
        document.UpdatedAt = DateTime.UtcNow;
        message.ProposalStatus = EditProposalStatus.Applied;

        await db.SaveChangesAsync();

        return new DocumentDetailResponse
        {
            Id = document.Id,
            FileName = document.FileName,
            FileSize = document.FileSize,
            UploadedAt = document.UploadedAt,
            UpdatedAt = document.UpdatedAt,
            Content = document.Content
        };
    }

    public async Task RejectProposalAsync(Guid documentId, Guid conversationId, Guid messageId, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

        var message = await LoadProposalMessageAsync(db, documentId, conversationId, messageId, userId);
        message.ProposalStatus = EditProposalStatus.Rejected;

        await db.SaveChangesAsync();
    }

    private static async Task<ChatMessage> LoadProposalMessageAsync(
        LuminaDbContext db, Guid documentId, Guid conversationId, Guid messageId, Guid userId)
    {
        var message = await db.ChatMessages
            .Include(m => m.Conversation)
            .ThenInclude(c => c.Document)
            .FirstOrDefaultAsync(m =>
                m.Id == messageId
                && m.ConversationId == conversationId
                && m.Conversation.DocumentId == documentId
                && m.Conversation.Document.UserId == userId);

        if (message?.ProposalTarget is null)
        {
            throw new KeyNotFoundException("Proposal not found.");
        }

        if (message.ProposalStatus != EditProposalStatus.Pending)
        {
            throw new InvalidOperationException("This proposal has already been applied or rejected.");
        }

        return message;
    }

    // Counts occurrences (including overlapping ones) of a passage in the
    // document, stopping at 2 — callers only care about "none / one / many".
    private static int CountOccurrences(string content, string target)
    {
        if (target.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var at = content.IndexOf(target, StringComparison.Ordinal);
        while (at >= 0 && count < 2)
        {
            count++;
            at = content.IndexOf(target, at + 1, StringComparison.Ordinal);
        }

        return count;
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
        sb.AppendLine(
            "You can also propose edits to the document when the user explicitly asks you to " +
            "change, rewrite, correct, expand or delete part of it. To propose an edit, first " +
            "write a short reply explaining the change, then END your reply with this exact block:");
        sb.AppendLine(EditProposalStreamFilter.TargetMarker);
        sb.AppendLine("(the passage to replace, copied EXACTLY, character for character, from the document)");
        sb.AppendLine(EditProposalStreamFilter.ReplacementMarker);
        sb.AppendLine("(the new text that replaces that passage)");
        sb.AppendLine(EditProposalStreamFilter.EndMarker);
        sb.AppendLine("Rules for edits:");
        sb.AppendLine(
            "- The target must be copied verbatim from the document — do not paraphrase, " +
            "reformat, or fix anything inside it.");
        sb.AppendLine(
            "- Keep the target as short as possible while still being unique within the document.");
        sb.AppendLine("- To delete a passage, leave the replacement section empty.");
        sb.AppendLine("- Propose at most ONE edit per reply; nothing may come after the end marker.");
        sb.AppendLine(
            "- An edit is only a proposal — the user reviews and applies it themselves. Never " +
            "claim the document has already been changed.");
        sb.AppendLine(
            "- For ordinary questions, or if the user hasn't clearly asked for a change, just " +
            "answer normally without this block.");
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
