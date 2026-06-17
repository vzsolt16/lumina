using Lumina.DTOs.AI;

namespace Lumina.Services.AI;

public class OllamaService : IAiService
{
    private readonly HttpClient _httpClient;

    public OllamaService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        var request = new OllamaGenerateRequest
        {
            Model = "qwen2.5:3b", // Smaller model as recommended for speed
            Prompt = prompt,
            Stream = false,
            Options = new OllamaOptions
            {
                NumPredict = 200 // Limit tokens for faster response
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