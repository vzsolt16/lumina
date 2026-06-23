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
}