using System.Text.Json;
using Lumina.DTOs.AI;

namespace Lumina.Services.AI;

public class OllamaService : IAiService
{
    private readonly HttpClient _httpClient;

    private static readonly JsonElement QuizSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "questions": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "question":      { "type": "string" },
                            "answerA":       { "type": "string" },
                            "answerB":       { "type": "string" },
                            "answerC":       { "type": "string" },
                            "answerD":       { "type": "string" },
                            "correctAnswer": { "type": "string", "enum": ["A", "B", "C", "D"] }
                        },
                        "required": ["question", "answerA", "answerB", "answerC", "answerD", "correctAnswer"]
                    }
                }
            },
            "required": ["questions"]
        }
        """).RootElement;

    public OllamaService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        var request = new OllamaGenerateRequest
        {
            Model = "qwen3:4b",
            Prompt = prompt,
            Stream = false,
            Think = false,
            Format = QuizSchema,
            Options = new OllamaOptions
            {
                NumPredict = 1200
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