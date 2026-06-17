using System.Text.Json;
using Lumina.DTOs.AI;

namespace Lumina.Services.AI;

public class OllamaService : IAiService
{
    private readonly HttpClient _httpClient;

    public OllamaService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GenerateAsync(string prompt, JsonElement? schema = null, int maxTokens = 2000)
    {
        var request = new OllamaGenerateRequest
        {
            Model = "qwen3:4b",
            Prompt = prompt,
            Stream = false,
            Think = false,
            Format = schema,
            Options = new OllamaOptions
            {
                NumPredict = maxTokens
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/api/generate",
            request);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<OllamaGenerateResponse>();

        if (result is null)
        {
            throw new InvalidOperationException(
                "Failed to deserialize Ollama response.");
        }

        return result.Response;
    }
}