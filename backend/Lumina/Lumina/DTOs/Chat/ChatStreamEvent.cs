using Lumina.DTOs.Document;

namespace Lumina.DTOs.Chat;

// One item of the ChatHub answer stream. The stream used to be plain strings;
// it is now typed so more than tokens can ride alongside the answer:
//   { type: "token", text }        — a fragment of the assistant's visible reply
//   { type: "proposal", proposal } — an edit proposal, emitted once at the end
//   { type: "document", document } — the document after a tool applied a change
//                                    (e.g. rename), so the client can refresh
//   { type: "tool", toolName, toolLabel, toolStatus } — progress for a tool call
//                                    the model made, so the UI can show e.g.
//                                    "Renaming document…" (status "running" → "done")
public class ChatStreamEvent
{
    public const string TokenType = "token";
    public const string ProposalType = "proposal";
    public const string DocumentType = "document";
    public const string ToolType = "tool";

    public const string ToolRunning = "running";
    public const string ToolDone = "done";

    public string Type { get; set; } = string.Empty;

    public string? Text { get; set; }

    public EditProposalResponse? Proposal { get; set; }

    public DocumentDetailResponse? Document { get; set; }

    public string? ToolName { get; set; }
    public string? ToolLabel { get; set; }
    public string? ToolStatus { get; set; }

    public static ChatStreamEvent ForToken(string text) =>
        new() { Type = TokenType, Text = text };

    public static ChatStreamEvent ForProposal(EditProposalResponse proposal) =>
        new() { Type = ProposalType, Proposal = proposal };

    public static ChatStreamEvent ForDocument(DocumentDetailResponse document) =>
        new() { Type = DocumentType, Document = document };

    public static ChatStreamEvent ForTool(string name, string label, string status) =>
        new() { Type = ToolType, ToolName = name, ToolLabel = label, ToolStatus = status };
}
