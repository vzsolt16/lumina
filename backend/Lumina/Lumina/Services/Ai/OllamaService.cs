using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lumina.DTOs.AI;
using Microsoft.Extensions.Configuration;

namespace Lumina.Services.AI;

public class OllamaService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _model = configuration["Ollama:Model"]
            ?? throw new InvalidOperationException("Ollama:Model is not configured.");
    }

    public async Task<string> GenerateAsync(string prompt, JsonElement? schema = null, int maxTokens = 2000)
    {
        var request = new OllamaGenerateRequest
        {
            Model = _model,
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

        // A non-streaming response should always come back complete. If it
        // doesn't — or if generation was cut off at the token cap (done_reason
        // "length") — the JSON is almost certainly truncated, so fail loudly
        // rather than parse a partial result into a half-empty quiz/flashcard set.
        if (!result.Done)
        {
            throw new InvalidOperationException(
                "Ollama returned an incomplete response.");
        }

        if (result.DoneReason == "length")
        {
            throw new InvalidOperationException(
                $"Ollama output was truncated at the token limit ({maxTokens}); increase maxTokens or shorten the input.");
        }

        return result.Response;
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        string prompt,
        int maxTokens = 2000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new OllamaGenerateRequest
        {
            Model = _model,
            Prompt = prompt,
            Stream = true,
            Think = false,
            Options = new OllamaOptions
            {
                NumPredict = maxTokens
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        {
            Content = JsonContent.Create(request)
        };

        // ResponseHeadersRead lets us start reading the body before the whole
        // response is buffered — essential for streaming token-by-token.
        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // Ollama streams newline-delimited JSON: one object per line, each carrying
        // a token fragment in `response`, until a final line with done = true.
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            OllamaGenerateResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaGenerateResponse>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(chunk.Response))
            {
                yield return chunk.Response;
            }

            if (chunk.Done)
            {
                break;
            }
        }
    }
}