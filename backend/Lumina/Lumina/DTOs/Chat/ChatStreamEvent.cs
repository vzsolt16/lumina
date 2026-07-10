namespace Lumina.DTOs.Chat;

// One item of the ChatHub answer stream. The stream used to be plain strings;
// it is now typed so an edit proposal can ride alongside the answer tokens:
//   { type: "token", text }        — a fragment of the assistant's visible reply
//   { type: "proposal", proposal } — an edit proposal, emitted once at the end
public class ChatStreamEvent
{
    public const string TokenType = "token";
    public const string ProposalType = "proposal";

    public string Type { get; set; } = string.Empty;

    public string? Text { get; set; }

    public EditProposalResponse? Proposal { get; set; }

    public static ChatStreamEvent ForToken(string text) =>
        new() { Type = TokenType, Text = text };

    public static ChatStreamEvent ForProposal(EditProposalResponse proposal) =>
        new() { Type = ProposalType, Proposal = proposal };
}
