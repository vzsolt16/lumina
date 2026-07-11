using System.Text.Json;

namespace Lumina.Services.AI;

public interface IAiService
{
    Task<string> GenerateAsync(string prompt, JsonElement? schema = null, int maxTokens = 2000);

    // Streams the model's answer token-by-token as it is generated. Used by the
    // chat feature so the UI can render the reply as it arrives. No structured
    // output here — chat replies are free-form prose.
    IAsyncEnumerable<string> GenerateStreamAsync(
        string prompt,
        int maxTokens = 2000,
        CancellationToken cancellationToken = default);

    // Streams a tool-capable chat turn. Yields content fragments as they arrive
    // and, once per turn, the tool calls the model decided to make. Callers run an
    // agent loop: stream a turn, execute any tool calls, append each result as a
    // "tool" message, and stream the next turn until a turn makes no tool calls.
    IAsyncEnumerable<AiChatDelta> ChatStreamAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiTool> tools,
        int maxTokens = 4000,
        CancellationToken cancellationToken = default);
}