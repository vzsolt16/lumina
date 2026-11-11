using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumina.Data;
using Lumina.DTOs.Chat;
using Lumina.DTOs.Document;
using Lumina.Models;
using Lumina.Services.AI;
using Lumina.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Lumina.Services.Chat;

public class ChatService : IChatService
{
    // A streamed answer can take minutes, which is far longer than a normal
    // request scope should hold a DbContext open. So instead of injecting a
    // scoped LuminaDbContext, we create a fresh short-lived scope per DB
    // operation — the same convention the background workers use (see CLAUDE.md).
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAiService _aiService;
    private readonly IDocumentContentConverter _contentConverter;
    private readonly IDocumentContextBuilder _contextBuilder;
    private readonly IDocumentEditorService _documentEditor;
    private readonly int _numCtx;

    public ChatService(
        IServiceScopeFactory scopeFactory,
        IAiService aiService,
        IDocumentContentConverter contentConverter,
        IDocumentContextBuilder contextBuilder,
        IDocumentEditorService documentEditor,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _aiService = aiService;
        _contentConverter = contentConverter;
        _contextBuilder = contextBuilder;
        _documentEditor = documentEditor;
        _numCtx = configuration.GetValue<int?>("Ollama:NumCtx") ?? 8192;
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
                    Kind = m.ProposalKind ?? ProposalKindReplace,
                    ContentVersion = m.ProposalContentVersion
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
        int contentVersion;
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
            contentVersion = conversation.Document.ContentVersion;

            history = await db.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(cancellationToken);

            isFirstMessage = history.Count == 0;
        }

        var messages = BuildMessages(fileName, content, history, question);
        var tools = new List<AiTool>
        {
            RenameDocumentTool, GetDocumentStructureTool, GetDocumentTextTool,
            GetDocumentNodeTool, ReplaceDocumentNodeTool, InsertDocumentNodeTool,
            DeleteDocumentNodeTool, MoveDocumentNodeTool,
            PrependTextTool, AppendTextTool
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
        string? proposalOperationJson = null;
        int? proposalContentVersion = null;
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

                    if (delta.Content is { Length: > 0 } token)
                    {
                        // Buffer the whole turn. Ollama/Qwen can emit explanatory
                        // text alongside a tool call; it is tool chatter, not a
                        // user-facing answer, and must not be streamed prematurely.
                        rawTurnText.Append(token);
                    }
                }

                // No tool calls → this turn was the visible answer; stop looping.
                if (toolCalls.Count == 0)
                {
                    var chunk = rawTurnText.ToString();
                    if (!seenContent)
                    {
                        chunk = chunk.TrimStart();
                    }

                    if (chunk.Length > 0)
                    {
                        seenContent = true;
                        visible.Append(chunk);
                        yield return ChatStreamEvent.ForToken(chunk);
                    }

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
                        call, documentId, userId, content, contentVersion, question, cancellationToken);

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
                            proposalOperationJson = draft.OperationJson;
                            proposalContentVersion = draft.ContentVersion;
                            proposalEvent = new EditProposalResponse
                            {
                                MessageId = assistantMessageId,
                                Target = draft.Target,
                                Replacement = draft.Replacement,
                                Status = EditProposalStatus.Pending,
                                Kind = draft.Kind
                                ,
                                ContentVersion = draft.ContentVersion
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
                    ProposalKind = proposalKind,
                    ProposalOperationJson = proposalOperationJson,
                    ProposalContentVersion = proposalContentVersion
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
        if (message.ProposalOperationJson is not null)
        {
            var operationJson = ResolveStructuredOperationReferences(
                document.ContentJson,
                message.ProposalOperationJson);
            var operation = DeserializeOperation(
                operationJson,
                message.ProposalContentVersion ?? document.ContentVersion);
            var result = await _documentEditor.ApplyOperationAsync(
                document.Id,
                userId,
                operation);
            message.ProposalStatus = EditProposalStatus.Applied;
            await db.SaveChangesAsync();
            return result;
        }

        var replacement = message.ProposalReplacement ?? string.Empty;
        var newContent = message.ProposalKind switch
        {
            ProposalKindPrepend => replacement + document.Content,
            ProposalKindInsert => ApplyInsert(document.Content, message.ProposalTarget!, replacement),
            _ => ApplyReplace(document.Content, message.ProposalTarget!, replacement)
        };

        DocumentContentMutation.Apply(document, newContent, _contentConverter);
        document.UpdatedAt = DateTime.UtcNow;
        message.ProposalStatus = EditProposalStatus.Applied;

        await db.SaveChangesAsync();

        return new DocumentDetailResponse
        {
            Id = document.Id,
            FileName = document.FileName,
            FileSize = document.FileSize,
            ContentSize = document.ContentSize,
            ContentVersion = document.ContentVersion,
            UploadedAt = document.UploadedAt,
            UpdatedAt = document.UpdatedAt,
            Content = document.Content,
            ContentJson = document.ContentJson
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

    private static readonly AiTool GetDocumentStructureTool = new(
        "get_document_structure",
        "Retrieve the document structure and stable node IDs before planning a structured edit.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
            additionalProperties = false
        }));

    private static readonly AiTool GetDocumentTextTool = new(
        "get_document_text",
        "Retrieve readable document text. Set include_ids to true when you need stable node IDs for editing.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                include_ids = new { type = "boolean" }
            }
        }));

    private static readonly AiTool GetDocumentNodeTool = new(
        "get_document_node",
        "Retrieve one document node by its stable ID, including its structure and text.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                node_id = new { type = "string" }
            },
            required = new[] { "node_id" }
        }));

    private static readonly AiTool ReplaceDocumentNodeTool = new(
        "replace_document_node",
        "Propose replacing the text of an existing paragraph, heading, or code block. Requires a stable node ID. The user must approve the proposal.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                target_id = new { type = "string" },
                new_text = new { type = "string" }
            },
            required = new[] { "target_id", "new_text" }
        }));

    private static readonly AiTool InsertDocumentNodeTool = new(
        "insert_document_node",
        "Propose inserting a new paragraph, heading, or code block. The user must approve the proposal.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                parent_id = new { type = "string" },
                index = new { type = "integer" },
                node_type = new { type = "string", @enum = new[] { "paragraph", "heading", "codeBlock" } },
                content = new { type = "string" }
            },
            required = new[] { "index", "node_type", "content" }
        }));

    private static readonly AiTool DeleteDocumentNodeTool = new(
        "delete_document_node",
        "Propose deleting an existing document node by stable ID. The user must approve the proposal.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { target_id = new { type = "string" } },
            required = new[] { "target_id" }
        }));

    private static readonly AiTool MoveDocumentNodeTool = new(
        "move_document_node",
        "Propose moving an existing block node. To move before or after another block node, pass "
        + "that node's ID as parent_id and set placement to before or after. To move into a "
        + "container, pass the container ID, placement at_index, and the zero-based child index. "
        + "For the document root, use parent_id=null and placement at_index. The user must "
        + "approve the proposal.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                target_id = new { type = "string" },
                parent_id = new { type = "string" },
                index = new { type = "integer" },
                placement = new { type = "string", @enum = new[] { "before", "after", "at_index" } }
            },
            required = new[] { "target_id", "index" }
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

    private static readonly AiTool PrependTextTool = new(
        "prepend_text",
        "Add new text to the START of the document. Use when the user asks to add an "
        + "introduction, title, preface, or other text before the existing content. The change "
        + "is proposed for the user to review, not applied directly.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                text = new
                {
                    type = "string",
                    description = "The new text to add at the start."
                }
            },
            required = new[] { "text" }
        }));

    // ProposalKind values shared with the client (see EditProposalResponse.Kind).
    private const string ProposalKindReplace = "replace";
    private const string ProposalKindInsert = "insert";
    private const string ProposalKindPrepend = "prepend";

    // A change a content tool proposes, folded into the pending proposal the user
    // reviews. Target is the passage to replace / the anchor to insert after (empty
    // to append); Kind is ProposalKindReplace or ProposalKindInsert.
    private readonly record struct ProposalDraft(
        string Target,
        string Replacement,
        string Kind,
        string? OperationJson = null,
        int? ContentVersion = null);

    private sealed record StoredOperation(
        string Kind,
        string? TargetId,
        string? ParentId,
        int Index,
        string? NodeType,
        string? Content,
        string? Placement = null);

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
            case "prepend_text":
                return "Proposing an introduction";
            case "get_document_structure":
                return "Reading document structure";
            case "get_document_text":
                return "Reading document text";
            case "get_document_node":
                return "Reading document node";
            case "replace_document_node":
                return "Proposing a node edit";
            case "insert_document_node":
                return "Proposing a node insertion";
            case "delete_document_node":
                return "Proposing a node deletion";
            case "move_document_node":
                return "Proposing a node move";

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
        AiToolCall call,
        Guid documentId,
        Guid userId,
        string content,
        int contentVersion,
        string question,
        CancellationToken cancellationToken)
    {
        switch (call.Name)
        {
            case "get_document_structure":
            case "get_document_text":
            case "get_document_node":
                return await ExecuteRetrievalToolAsync(
                    call, documentId, userId, cancellationToken);

            case "replace_document_node":
            {
                if (!TryGetStringArg(call, "target_id", out var targetId)
                    || !TryGetStringArg(call, "new_text", out var newText))
                {
                    return ("Error: target_id and new_text are required.", null, null);
                }

                var operation = new StoredOperation(
                    "replace",
                    targetId,
                    null,
                    0,
                    null,
                    newText);
                return CreateStructuredProposal(
                    "Proposed replacing the node text. The user will review and apply it.",
                    targetId,
                    newText,
                    "replace_node",
                    operation,
                    contentVersion);
            }

            case "insert_document_node":
            {
                if (!TryGetOptionalStringArg(call, "parent_id", out var parentId)
                    || !TryGetIntArg(call, "index", out var index)
                    || !TryGetStringArg(call, "node_type", out var nodeType)
                    || !TryGetStringArg(call, "content", out var nodeContent))
                {
                    return ("Error: index, node_type, and content are required.", null, null);
                }

                var operation = new StoredOperation(
                    "insert", null, parentId, index, nodeType, nodeContent);
                return CreateStructuredProposal(
                    "Proposed inserting the new node. The user will review and apply it.",
                    parentId ?? string.Empty,
                    nodeContent,
                    "insert_node",
                    operation,
                    contentVersion);
            }

            case "delete_document_node":
            {
                if (!TryGetStringArg(call, "target_id", out var targetId))
                {
                    return ("Error: target_id is required.", null, null);
                }

                var requestedTarget = await ResolveDeleteTargetFromQuestionAsync(
                    documentId, userId, question, cancellationToken);
                var resolvedTarget = requestedTarget?.Id
                    ?? await ResolveNodeIdAsync(documentId, userId, targetId, cancellationToken)
                    ?? targetId;
                var operation = new StoredOperation("delete", resolvedTarget, null, 0, null, null);
                var targetLabel = requestedTarget?.Label
                    ?? await GetNodeLabelAsync(documentId, userId, resolvedTarget, cancellationToken)
                    ?? targetId;
                return CreateStructuredProposal(
                    "Proposed deleting the node. The user will review and apply it.",
                    targetLabel,
                    string.Empty,
                    "delete_node",
                    operation,
                    contentVersion);
            }

            case "move_document_node":
            {
                if (!TryGetStringArg(call, "target_id", out var targetId)
                    || !TryGetOptionalStringArg(call, "parent_id", out var parentId)
                    || !TryGetIntArg(call, "index", out var index))
                {
                    return ("Error: target_id and index are required.", null, null);
                }

                var placement = TryGetOptionalStringArg(call, "placement", out var requestedPlacement)
                    ? requestedPlacement
                    : "after";
                if (placement is not ("before" or "after" or "at_index"))
                {
                    return ("Error: placement must be before, after, or at_index.", null, null);
                }

                var operation = new StoredOperation(
                    "move", targetId, parentId, index, null, null, placement);
                var targetLabel = await GetNodeLabelAsync(documentId, userId, targetId, cancellationToken)
                    ?? targetId;
                var destinationLabel = parentId is null
                    ? "the beginning of the document"
                    : await GetNodeLabelAsync(documentId, userId, parentId, cancellationToken)
                        ?? parentId;
                return CreateStructuredProposal(
                    "Proposed moving the node. The user will review and apply it.",
                    targetLabel,
                    destinationLabel,
                    "move_node",
                    operation,
                    contentVersion);
            }

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
                    try
                    {
                        newName = DocumentValidation.PreserveExtension(document.FileName, newName);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return ($"Error: {ex.Message}", null, null);
                    }

                    if (!string.Equals(document.FileName, newName, StringComparison.Ordinal))
                    {
                        document.FileName = newName;
                        document.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                    }

                    return ($"Done — the document is now named \"{newName}\".", new DocumentDetailResponse
                    {
                        Id = document.Id,
                        FileName = document.FileName,
                        FileSize = document.FileSize,
                        ContentSize = document.ContentSize,
                        ContentVersion = document.ContentVersion,
                        UploadedAt = document.UploadedAt,
                        UpdatedAt = document.UpdatedAt,
                        Content = document.Content,
                        ContentJson = document.ContentJson
                    }, null);
                }

            case "prepend_text":
            case "append_text":
            {
                if (!TryGetStringArg(call, "text", out var addedText) || addedText.Trim().Length == 0)
                {
                    return ("Error: the text argument was missing or empty.", null, null);
                }
                var location = call.Name == "prepend_text" ? "start" : "end";
                return (
                    $"Proposed adding the text to the {location} of the document. The user will review and "
                    + "apply it — confirm briefly, and don't claim it's already done.",
                    null,
                    new ProposalDraft(
                        string.Empty,
                        addedText.Trim(),
                        call.Name == "prepend_text" ? ProposalKindPrepend : ProposalKindInsert));
            }

            default:
                return ($"Error: unknown tool '{call.Name}'.", null, null);
        }
    }

    private async Task<(string ResultText, DocumentDetailResponse? UpdatedDoc, ProposalDraft? Proposal)>
        ExecuteRetrievalToolAsync(
            AiToolCall call,
            Guid documentId,
            Guid userId,
            CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();
        var document = await db.Documents.FirstOrDefaultAsync(
            d => d.Id == documentId && d.UserId == userId,
            cancellationToken);
        if (document is null)
        {
            return ("Error: the document no longer exists.", null, null);
        }

        if (call.Name == "get_document_structure")
        {
            var outline = await _contextBuilder.BuildAsync(
                document,
                DocumentContextType.StructureOutline,
                cancellationToken);
            return (
                LimitContext(outline, 12000),
                null,
                null);
        }

        if (call.Name == "get_document_text")
        {
            var includeIds = TryGetBoolArg(call, "include_ids");
            return (
                LimitContext(await _contextBuilder.BuildAsync(
                    document,
                    includeIds ? DocumentContextType.TextWithIds : DocumentContextType.PlainText,
                    cancellationToken), 12000),
                null,
                null);
        }

        if (!TryGetStringArg(call, "node_id", out var nodeId))
        {
            return ("Error: node_id is required.", null, null);
        }

        var structure = await _contextBuilder.BuildAsync(
            document,
            DocumentContextType.StructuredJson,
            cancellationToken);
        var root = JsonNode.Parse(structure)!.AsObject();
        var node = FindJsonNode(root, nodeId);
        return node is null
            ? ("Error: node_id was not found.", null, null)
            : (LimitContext(node.ToJsonString(), 16000), null, null);
    }

    private static (string, DocumentDetailResponse?, ProposalDraft?) CreateStructuredProposal(
        string message,
        string target,
        string replacement,
        string kind,
        StoredOperation operation,
        int contentVersion) =>
        (message, null, new ProposalDraft(
            target,
            replacement,
            kind,
            JsonSerializer.Serialize(operation),
            contentVersion));

    private async Task<string?> GetNodeLabelAsync(
        Guid documentId,
        Guid userId,
        string reference,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();
        var document = await db.Documents.FirstOrDefaultAsync(
            d => d.Id == documentId && d.UserId == userId,
            cancellationToken);
        if (document is null)
        {
            return null;
        }

        var root = JsonNode.Parse(document.ContentJson)?.AsObject();
        if (root is null)
        {
            return null;
        }

        var node = FindJsonNode(root, reference);
        if (node is null)
        {
            var resolved = ResolveNodeReference(root, reference);
            node = resolved is null ? null : FindJsonNode(root, resolved);
        }

        return node is null ? null : ExtractNodeText(node);
    }

    private async Task<(string Id, string Label)?> ResolveDeleteTargetFromQuestionAsync(
        Guid documentId,
        Guid userId,
        string question,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();
        var document = await db.Documents.FirstOrDefaultAsync(
            d => d.Id == documentId && d.UserId == userId,
            cancellationToken);
        var root = document is null
            ? null
            : JsonNode.Parse(document.ContentJson)?.AsObject();
        if (root is null)
        {
            return null;
        }

        var matches = FindBlockNodes(root)
            .Select(node => new
            {
                Id = node["attrs"]?["id"]?.GetValue<string>(),
                Label = ExtractNodeText(node)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                && !string.IsNullOrWhiteSpace(item.Label)
                && question.Contains(item.Label, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count == 1
            ? (matches[0].Id!, matches[0].Label)
            : null;
    }

    private async Task<string?> ResolveNodeIdAsync(
        Guid documentId,
        Guid userId,
        string reference,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();
        var document = await db.Documents.FirstOrDefaultAsync(
            d => d.Id == documentId && d.UserId == userId,
            cancellationToken);
        var root = document is null
            ? null
            : JsonNode.Parse(document.ContentJson)?.AsObject();
        return root is null ? null : ResolveNodeReference(root, reference);
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

    private static bool TryGetOptionalStringArg(AiToolCall call, string name, out string? value)
    {
        value = null;
        if (call.Arguments.ValueKind != JsonValueKind.Object
            || !call.Arguments.TryGetProperty(name, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return true;
    }

    private static bool TryGetIntArg(AiToolCall call, string name, out int value)
    {
        value = 0;
        return call.Arguments.ValueKind == JsonValueKind.Object
            && call.Arguments.TryGetProperty(name, out var element)
            && element.TryGetInt32(out value);
    }

    private static bool TryGetBoolArg(AiToolCall call, string name) =>
        call.Arguments.ValueKind == JsonValueKind.Object
        && call.Arguments.TryGetProperty(name, out var element)
        && element.ValueKind == JsonValueKind.True;

    private static JsonObject? FindJsonNode(JsonObject node, string id)
    {
        if (string.Equals(node["attrs"]?["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
        {
            return node;
        }

        return node["content"] is JsonArray content
            ? content.OfType<JsonObject>()
                .Select(child => FindJsonNode(child, id))
                .FirstOrDefault(found => found is not null)
            : null;
    }

    private static DocumentOperation DeserializeOperation(string json, int version)
    {
        var operation = JsonSerializer.Deserialize<StoredOperation>(json)
            ?? throw new InvalidOperationException("The saved edit operation is invalid.");

        return operation.Kind switch
        {
            "replace" => new ReplaceDocumentNode(version, operation.TargetId!, operation.Content ?? string.Empty),
            "insert" => new InsertDocumentNode(
                version,
                operation.ParentId,
                operation.Index,
                operation.NodeType!,
                operation.Content ?? string.Empty),
            "delete" => new DeleteDocumentNode(version, operation.TargetId!),
            "move" => new MoveDocumentNode(
                version,
                operation.TargetId!,
                operation.ParentId,
                operation.Index,
                operation.Placement ?? "after"),
            _ => throw new InvalidOperationException("The saved edit operation is unsupported.")
        };
    }

    private static string ResolveStructuredOperationReferences(
        string contentJson,
        string operationJson)
    {
        var operation = JsonSerializer.Deserialize<StoredOperation>(operationJson)
            ?? throw new InvalidOperationException("The saved edit operation is invalid.");

        if (operation.Kind is not ("replace" or "delete" or "move"))
        {
            return operationJson;
        }

        var root = JsonNode.Parse(contentJson)?.AsObject()
            ?? throw new InvalidOperationException("The document structure is invalid.");
        var targetId = ResolveNodeReference(root, operation.TargetId);
        var parentId = ResolveNodeReference(root, operation.ParentId);

        if (targetId is null)
        {
            throw new InvalidOperationException(
                "The target node could not be found. The document may have changed.");
        }

        if (operation.ParentId is not null && parentId is null)
        {
            throw new InvalidOperationException(
                "The destination node could not be found. The document may have changed.");
        }

        return JsonSerializer.Serialize(operation with
        {
            TargetId = targetId,
            ParentId = parentId
        });
    }

    private static string? ResolveNodeReference(JsonObject root, string? reference)
    {
        if (reference is null)
        {
            return null;
        }

        var byId = FindJsonNode(root, reference);
        if (byId is not null)
        {
            return reference;
        }

        var matches = FindBlockNodes(root)
            .Where(node => string.Equals(
                ExtractNodeText(node),
                reference.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count == 1
            ? matches[0]["attrs"]?["id"]?.GetValue<string>()
            : null;
    }

    private static IEnumerable<JsonObject> FindBlockNodes(JsonObject node)
    {
        var type = node["type"]?.GetValue<string>();
        if (type is not null and not "doc" and not "text")
        {
            yield return node;
        }

        if (node["content"] is not JsonArray content)
        {
            yield break;
        }

        foreach (var child in content.OfType<JsonObject>())
        {
            foreach (var descendant in FindBlockNodes(child))
            {
                yield return descendant;
            }
        }
    }

    private static string ExtractNodeText(JsonObject node)
    {
        var builder = new StringBuilder();
        AppendNodeText(node, builder);
        return builder.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static void AppendNodeText(JsonObject node, StringBuilder builder)
    {
        if (node["type"]?.GetValue<string>() == "text")
        {
            builder.Append(node["text"]?.GetValue<string>());
            return;
        }

        if (node["content"] is JsonArray content)
        {
            foreach (var child in content.OfType<JsonObject>())
            {
                AppendNodeText(child, builder);
            }
        }
    }

    // The document is injected wholesale for now ("start simple"). When RAG lands,
    // only this method changes: swap the document content for the retrieved chunks.
    private List<AiMessage> BuildMessages(
        string fileName,
        string content,
        List<ChatMessage> history,
        string question)
    {
        var sb = new StringBuilder();
        var contentBudget = Math.Clamp((_numCtx - 8000) * 2, 8000, 16000);
        var boundedContent = LimitContext(content, contentBudget);

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
            "For structured changes, first call get_document_structure or get_document_text with " +
            "include_ids=true. Use the returned stable node IDs with the structured editing tools. " +
            "Structured edits are proposed for approval and are never applied immediately.");
        sb.AppendLine(
            "To move a paragraph or section above/below another block, call move_document_node with " +
            "target_id set to the node being moved, parent_id set to the destination block's ID, " +
            "and placement set to before or after. To move into a container, use placement at_index " +
            "with the container ID and zero-based index; use parent_id=null for the document root.");
        sb.AppendLine(
            "Never show stable node IDs in your user-facing answer. Use them only as tool arguments; " +
            "refer to the affected section by its readable text or a short description.");
        sb.AppendLine(
            "- To ADD text to the start, call prepend_text. To ADD text to the end (e.g. \"add a " +
            "summary\", \"append a conclusion\"), call append_text.");
        sb.AppendLine(
            "Use replace_document_node to change or delete an existing paragraph, heading, or code " +
            "block; use insert_document_node to add a new block anywhere; use delete_document_node " +
            "to remove a block; and use move_document_node to reorder blocks.");
        sb.AppendLine(
            "- To rename the document, call rename_document.");
        sb.AppendLine(
            "All content changes are proposed for review and approval; never say the document has " +
            "already changed. Rename applies immediately. Make at most ONE change per reply. For " +
            "ordinary questions, just answer normally without calling any tool.");
        sb.AppendLine();
        sb.AppendLine($"--- DOCUMENT: {fileName} ---");
        sb.AppendLine(boundedContent);
        sb.AppendLine("--- END DOCUMENT ---");
        var messages = new List<AiMessage>
        {
            new("system", sb.ToString())
        };

        var historyChars = 6000;
        var selectedHistory = new List<ChatMessage>();
        for (var i = history.Count - 1; i >= 0 && historyChars > 0; i--)
        {
            var message = history[i];
            if (message.Content.Length > historyChars && selectedHistory.Count > 0)
            {
                break;
            }

            selectedHistory.Add(message);
            historyChars -= message.Content.Length;
        }
        selectedHistory.Reverse();

        foreach (var message in selectedHistory)
        {
            var role = message.Role == "assistant" ? "assistant" : "user";
            messages.Add(new AiMessage(role, message.Content));
        }

        messages.Add(new AiMessage("user", question));

        return messages;
    }

    private static string LimitContext(string value, int maxChars = 24000)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        var headLength = maxChars / 2;
        var tailLength = maxChars - headLength;
        return value[..headLength]
            + "\n\n[… document context truncated; use retrieval tools for omitted sections …]\n\n"
            + value[^tailLength..];
    }
}
