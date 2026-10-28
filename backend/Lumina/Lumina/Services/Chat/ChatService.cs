using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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
                    Status = m.ProposalStatus ?? EditProposalStatus.Pending,
                    Kind = m.ProposalKind ?? ProposalKindReplace
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
        Guid documentId;
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

            documentId = conversation.Document.Id;
            fileName = conversation.Document.FileName;
            content = conversation.Document.Content;

            history = await db.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(cancellationToken);

            isFirstMessage = history.Count == 0;
        }

        var messages = BuildMessages(fileName, content, history, question);
        var tools = new List<AiTool>
        {
            RenameDocumentTool, AppendTextTool, InsertTextTool, ReplaceTextTool
        };

        var askedAt = DateTime.UtcNow;
        // Generated up front so the proposal event streamed to the client refers
        // to the same message id the proposal is persisted under.
        var assistantMessageId = Guid.NewGuid();

        // What the user actually sees; this (not the tool chatter) is persisted as
        // the message.
        var visible = new StringBuilder();
        // Set when a content tool proposes a change — persisted on the message and
        // applied only when the user accepts it. At most one proposal per reply.
        string? proposalTarget = null;
        string? proposalReplacement = null;
        string? proposalKind = null;
        var proposalCreated = false;
        var completed = false;
        var seenContent = false;
        // Set once a tool mutates the document (rename), so we can still show a
        // confirmation if the model produces no visible reply.
        var toolMutatedDoc = false;

        try
        {
            // The model may take several turns: call a tool, get the result, then
            // produce its final answer. Loop until a turn makes no tool calls (the
            // visible answer). The cap is a backstop against a model that never
            // stops calling tools.
            const int maxTurns = 3;
            for (var turn = 0; turn < maxTurns; turn++)
            {
                var toolCalls = new List<AiToolCall>();
                // The raw content of this turn, kept so the assistant tool-call
                // message we send back mirrors what the model actually produced.
                var rawTurnText = new StringBuilder();

                // Headroom for the model's answer so a normal turn isn't cut off
                // mid-stream.
                await foreach (var delta in _aiService
                    .ChatStreamAsync(messages, tools, maxTokens: 4000, cancellationToken: cancellationToken))
                {
                    if (delta.ToolCalls is { Count: > 0 } calls)
                    {
                        toolCalls.AddRange(calls);
                    }

                    if (delta.Content is not { Length: > 0 } token)
                    {
                        continue;
                    }

                    rawTurnText.Append(token);

                    var chunk = token;

                    // Skip the leading whitespace the model emits before its first
                    // real content, so the answer doesn't start with blank lines.
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

                // No tool calls → this turn was the visible answer; stop looping.
                if (toolCalls.Count == 0)
                {
                    break;
                }

                // The model asked to use tools. Record its request, run each tool,
                // and feed the results back so the next turn can answer with them.
                messages.Add(new AiMessage("assistant", rawTurnText.ToString(), ToolCalls: toolCalls));

                foreach (var call in toolCalls)
                {
                    // Surface the tool call so the UI can show progress (e.g.
                    // "Renaming document…") during the pause before the reply.
                    var label = ToolActivityLabel(call);
                    yield return ChatStreamEvent.ForTool(call.Name, label, ChatStreamEvent.ToolRunning);

                    var (resultText, updatedDoc, proposalDraft) = await ExecuteToolAsync(
                        call, documentId, userId, content, cancellationToken);

                    // A content tool proposes (never directly applies) a change. Keep
                    // only the first proposal per reply and turn it into the pending
                    // proposal the client renders; tell the model it's proposed, not
                    // done, so it confirms rather than claiming success.
                    EditProposalResponse? proposalEvent = null;
                    if (proposalDraft is not null)
                    {
                        if (proposalCreated)
                        {
                            resultText = "Error: a change has already been proposed in this reply; "
                                + "only one is allowed. Do not propose another.";
                        }
                        else
                        {
                            var draft = proposalDraft.Value;
                            proposalCreated = true;
                            proposalTarget = draft.Target;
                            proposalReplacement = draft.Replacement;
                            proposalKind = draft.Kind;
                            proposalEvent = new EditProposalResponse
                            {
                                MessageId = assistantMessageId,
                                Target = draft.Target,
                                Replacement = draft.Replacement,
                                Status = EditProposalStatus.Pending,
                                Kind = draft.Kind
                            };
                        }
                    }

                    messages.Add(new AiMessage("tool", resultText, ToolName: call.Name));

                    yield return ChatStreamEvent.ForTool(call.Name, label, ChatStreamEvent.ToolDone);

                    // rename applies immediately; tell the client so the header and
                    // Content tab refresh.
                    if (updatedDoc is not null)
                    {
                        toolMutatedDoc = true;
                        yield return ChatStreamEvent.ForDocument(updatedDoc);
                    }

                    if (proposalEvent is not null)
                    {
                        yield return ChatStreamEvent.ForProposal(proposalEvent);
                    }
                }
            }

            // A tool proposed a change (or renamed) but the model gave no words
            // (e.g. it hit the turn cap): don't leave an empty reply bubble.
            if (visible.Length == 0)
            {
                var fallback = proposalCreated
                    ? "I've proposed the change above — review it and apply it when you're ready."
                    : toolMutatedDoc
                        ? "Done — I made the change."
                        : null;
                if (fallback is not null)
                {
                    visible.Append(fallback);
                    yield return ChatStreamEvent.ForToken(fallback);
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
                    ProposalStatus = proposalTarget is null ? null : EditProposalStatus.Pending,
                    ProposalKind = proposalKind
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

        // The document may have been edited since the proposal was made; each
        // path re-checks that the passage still exists (and is still unambiguous)
        // rather than blindly rewriting content.
        var replacement = message.ProposalReplacement ?? string.Empty;
        var newContent = message.ProposalKind == ProposalKindInsert
            ? ApplyInsert(document.Content, message.ProposalTarget!, replacement)
            : ApplyReplace(document.Content, message.ProposalTarget!, replacement);

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

    // Rewrites the single occurrence of target in content. Throws if the passage
    // no longer matches exactly or is no longer unique.
    private static string ApplyReplace(string content, string target, string replacement)
    {
        var occurrences = CountOccurrences(content, target);
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

        var index = content.IndexOf(target, StringComparison.Ordinal);
        return content.Remove(index, target.Length).Insert(index, replacement);
    }

    // Inserts text after the single occurrence of anchor, separated by a blank
    // line. An empty anchor appends to the end of the document. Throws if a
    // non-empty anchor no longer matches exactly or is no longer unique.
    private static string ApplyInsert(string content, string anchor, string text)
    {
        if (anchor.Length == 0)
        {
            return content.Length == 0 ? text : content.TrimEnd() + "\n\n" + text;
        }

        var occurrences = CountOccurrences(content, anchor);
        if (occurrences == 0)
        {
            throw new InvalidOperationException(
                "The document has changed since this edit was proposed; the passage to insert after no longer exists.");
        }
        if (occurrences > 1)
        {
            throw new InvalidOperationException(
                "The document has changed since this edit was proposed; the passage to insert after now appears more than once.");
        }

        var index = content.IndexOf(anchor, StringComparison.Ordinal) + anchor.Length;
        return content.Insert(index, "\n\n" + text);
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

    // Tools the model may call from chat. Kept minimal on purpose — one action,
    // end to end. The description/schema is how the model learns the tool exists.
    private static readonly AiTool RenameDocumentTool = new(
        "rename_document",
        "Rename the current document. Use only when the user explicitly asks to rename or "
        + "retitle it. Provide the complete new name.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                new_name = new
                {
                    type = "string",
                    description = "The new document name."
                }
            },
            required = new[] { "new_name" }
        }));

    // Content tools don't change the document directly — they propose an edit the
    // user reviews and applies (see ExecuteToolAsync). Structured arguments are far
    // more reliable for a small model than a freeform marker block in the reply.
    private static readonly AiTool AppendTextTool = new(
        "append_text",
        "Add new text to the END of the document (e.g. a summary or conclusion). Use when the "
        + "user asks to add, append, or write something new at the end. The change is proposed "
        + "for the user to review, not applied directly.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                text = new
                {
                    type = "string",
                    description = "The new text to add at the end of the document."
                }
            },
            required = new[] { "text" }
        }));

    private static readonly AiTool InsertTextTool = new(
        "insert_text",
        "Insert new text into the middle of the document, right after an existing passage. The "
        + "change is proposed for the user to review, not applied directly.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                after_text = new
                {
                    type = "string",
                    description = "A short passage copied EXACTLY from the document; the new text "
                        + "is inserted right after it. Must appear exactly once."
                },
                new_text = new
                {
                    type = "string",
                    description = "The new text to insert."
                }
            },
            required = new[] { "after_text", "new_text" }
        }));

    private static readonly AiTool ReplaceTextTool = new(
        "replace_text",
        "Replace an existing passage of the document with new text — use to rewrite, correct, "
        + "shorten, expand, or (with an empty replacement) delete it. The change is proposed for "
        + "the user to review, not applied directly.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                old_text = new
                {
                    type = "string",
                    description = "The passage to replace, copied EXACTLY from the document. Must "
                        + "appear exactly once."
                },
                new_text = new
                {
                    type = "string",
                    description = "The replacement text. Leave empty to delete the passage."
                }
            },
            required = new[] { "old_text", "new_text" }
        }));

    // ProposalKind values shared with the client (see EditProposalResponse.Kind).
    private const string ProposalKindReplace = "replace";
    private const string ProposalKindInsert = "insert";

    // A change a content tool proposes, folded into the pending proposal the user
    // reviews. Target is the passage to replace / the anchor to insert after (empty
    // to append); Kind is ProposalKindReplace or ProposalKindInsert.
    private readonly record struct ProposalDraft(string Target, string Replacement, string Kind);

    // A short human label for a tool call, shown to the user while the tool runs.
    private static string ToolActivityLabel(AiToolCall call)
    {
        switch (call.Name)
        {
            case "rename_document":
                if (call.Arguments.ValueKind == JsonValueKind.Object
                    && call.Arguments.TryGetProperty("new_name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    return $"Renaming document to “{name.GetString()!.Trim()}”";
                }
                return "Renaming document";

            case "append_text":
                return "Proposing an addition";
            case "insert_text":
                return "Proposing an insertion";
            case "replace_text":
                return "Proposing an edit";

            default:
                return "Working";
        }
    }

    // Runs a tool the model called. Returns the text fed back to the model, the
    // updated document if the tool changed it directly (rename), and a proposed
    // change if it's a content tool (applied later, only if the user accepts). Bad
    // arguments / unknown tools come back as an error string the model can recover
    // from — this never throws.
    private async Task<(string ResultText, DocumentDetailResponse? UpdatedDoc, ProposalDraft? Proposal)> ExecuteToolAsync(
        AiToolCall call, Guid documentId, Guid userId, string content, CancellationToken cancellationToken)
    {
        switch (call.Name)
        {
            case "rename_document":
                // Guard ValueKind first: TryGetProperty throws if Arguments isn't a
                // JSON object (e.g. the model emits a call with no arguments at all,
                // leaving a default/Undefined element).
                if (call.Arguments.ValueKind != JsonValueKind.Object
                    || !call.Arguments.TryGetProperty("new_name", out var nameElement)
                    || nameElement.ValueKind != JsonValueKind.String)
                {
                    return ("Error: the new_name argument was missing.", null, null);
                }

                // Mirror the validation in DocumentService.UpdateAsync.
                var newName = nameElement.GetString()!.Trim();
                if (newName.Length == 0)
                {
                    return ("Error: the name cannot be empty.", null, null);
                }
                if (newName.Length > 255)
                {
                    return ("Error: the name is too long (max 255 characters).", null, null);
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();

                    var document = await db.Documents
                        .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, cancellationToken);
                    if (document is null)
                    {
                        return ("Error: the document no longer exists.", null, null);
                    }

                    document.FileName = newName;
                    document.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);

                    return ($"Done — the document is now named \"{newName}\".", new DocumentDetailResponse
                    {
                        Id = document.Id,
                        FileName = document.FileName,
                        FileSize = document.FileSize,
                        UploadedAt = document.UploadedAt,
                        UpdatedAt = document.UpdatedAt,
                        Content = document.Content
                    }, null);
                }

            case "append_text":
            {
                if (!TryGetStringArg(call, "text", out var appendText) || appendText.Trim().Length == 0)
                {
                    return ("Error: the text argument was missing or empty.", null, null);
                }
                return (
                    "Proposed adding the text to the end of the document. The user will review and "
                    + "apply it — confirm briefly, and don't claim it's already done.",
                    null,
                    new ProposalDraft(string.Empty, appendText.Trim(), ProposalKindInsert));
            }

            case "insert_text":
            {
                if (!TryGetStringArg(call, "after_text", out var afterText) || afterText.Trim().Length == 0)
                {
                    return ("Error: the after_text argument was missing or empty.", null, null);
                }
                if (!TryGetStringArg(call, "new_text", out var insertText) || insertText.Trim().Length == 0)
                {
                    return ("Error: the new_text argument was missing or empty.", null, null);
                }
                var anchor = afterText.Trim();
                var anchorCount = CountOccurrences(content, anchor);
                if (anchorCount == 0)
                {
                    return ("Error: after_text was not found in the document. Copy a short passage "
                        + "exactly as it appears.", null, null);
                }
                if (anchorCount > 1)
                {
                    return ("Error: after_text appears more than once; quote a longer, unique passage.",
                        null, null);
                }
                return (
                    "Proposed inserting the text after that passage. The user will review and apply "
                    + "it — confirm briefly, and don't claim it's already done.",
                    null,
                    new ProposalDraft(anchor, insertText.Trim(), ProposalKindInsert));
            }

            case "replace_text":
            {
                if (!TryGetStringArg(call, "old_text", out var oldText) || oldText.Trim().Length == 0)
                {
                    return ("Error: the old_text argument was missing or empty.", null, null);
                }
                // new_text may be empty (a deletion), so only require it to be present.
                if (!TryGetStringArg(call, "new_text", out var replaceWith))
                {
                    return ("Error: the new_text argument was missing.", null, null);
                }
                var oldPassage = oldText.Trim();
                var newPassage = replaceWith.Trim();
                if (oldPassage == newPassage)
                {
                    return ("Error: old_text and new_text are identical; nothing to change.", null, null);
                }
                var oldCount = CountOccurrences(content, oldPassage);
                if (oldCount == 0)
                {
                    return ("Error: old_text was not found in the document. Copy a short passage "
                        + "exactly as it appears.", null, null);
                }
                if (oldCount > 1)
                {
                    return ("Error: old_text appears more than once; quote a longer, unique passage.",
                        null, null);
                }
                return (
                    "Proposed replacing that passage. The user will review and apply it — confirm "
                    + "briefly, and don't claim it's already done.",
                    null,
                    new ProposalDraft(oldPassage, newPassage, ProposalKindReplace));
            }

            default:
                return ($"Error: unknown tool '{call.Name}'.", null, null);
        }
    }

    // Extracts a string argument from a tool call, guarding against a missing or
    // non-object Arguments element (a model may emit a call with no arguments).
    private static bool TryGetStringArg(AiToolCall call, string name, out string value)
    {
        value = string.Empty;
        if (call.Arguments.ValueKind != JsonValueKind.Object
            || !call.Arguments.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = element.GetString() ?? string.Empty;
        return true;
    }

    // The document is injected wholesale for now ("start simple"). When RAG lands,
    // only this method changes: swap the document content for the retrieved chunks.
    private static List<AiMessage> BuildMessages(
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
            "When the user explicitly asks you to change the document, call the matching tool " +
            "instead of describing the change or writing the edited text in your reply:");
        sb.AppendLine(
            "- To ADD text to the end (e.g. \"add a summary\", \"append a conclusion\"), call " +
            "append_text with the new text. This is the usual way to add something.");
        sb.AppendLine(
            "- To CHANGE, rewrite, correct, shorten, expand, or delete existing text, call " +
            "replace_text with old_text (an exact quote from the document) and new_text (leave " +
            "new_text empty to delete).");
        sb.AppendLine(
            "- To INSERT text in the middle, call insert_text with after_text (an exact quote to " +
            "insert after) and new_text.");
        sb.AppendLine(
            "- To rename the document, call rename_document.");
        sb.AppendLine(
            "For replace_text and insert_text, copy the quoted passage VERBATIM from the document " +
            "(character for character) and keep it short but unique — the tool matches it exactly.");
        sb.AppendLine(
            "append_text, insert_text, and replace_text only PROPOSE a change; the user reviews and " +
            "applies it, so never say the document has been changed — just briefly say what you " +
            "proposed. Rename applies immediately. Make at most ONE change per reply. For ordinary " +
            "questions, just answer normally without calling any tool.");
        sb.AppendLine();
        sb.AppendLine($"--- DOCUMENT: {fileName} ---");
        sb.AppendLine(content);
        sb.AppendLine("--- END DOCUMENT ---");

        var messages = new List<AiMessage>
        {
            new("system", sb.ToString())
        };

        foreach (var message in history)
        {
            var role = message.Role == "assistant" ? "assistant" : "user";
            messages.Add(new AiMessage(role, message.Content));
        }

        messages.Add(new AiMessage("user", question));

        return messages;
    }
}
