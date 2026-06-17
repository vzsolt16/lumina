using System.Text.Json;

namespace Lumina.Services.AI;

public interface IAiService
{
    Task<string> GenerateAsync(string prompt, JsonElement? schema = null, int maxTokens = 2000);
}