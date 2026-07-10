namespace Lumina.Models;

// Lifecycle of an AI-proposed document edit attached to a chat message.
// Stored as a string on ChatMessage and sent verbatim to the frontend.
public static class EditProposalStatus
{
    public const string Pending = "pending";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
}
