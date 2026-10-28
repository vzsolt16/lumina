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
    private readonly int _numCtx;

    public OllamaService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _model = configuration["Ollama:Model"]
            ?? throw new InvalidOperationException("Ollama:Model is not configured.");
        // Ollama's default context is 4096, which the wholesale-injected document
        // overflows; override it (configurable so big documents can go higher).
        _numCtx = configuration.GetValue<int?>("Ollama:NumCtx") ?? 8192;
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
                NumPredict = maxTokens,
                NumCtx = _numCtx
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/api/generate",
            request);

        await EnsureOllamaSuccessAsync(response, CancellationToken.None);

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
                NumPredict = maxTokens,
                NumCtx = _numCtx
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

        await EnsureOllamaSuccessAsync(response, cancellationToken);

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

    public async IAsyncEnumerable<AiChatDelta> ChatStreamAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiTool> tools,
        int maxTokens = 4000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest
        {
            Model = _model,
            Stream = true,
            Think = false,
            Messages = messages.Select(ToOllama).ToList(),
            Tools = tools.Count == 0
                ? null
                : tools.Select(t => new OllamaTool
                {
                    Function = new OllamaFunction
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Parameters = t.Parameters
                    }
                }).ToList(),
            Options = new OllamaOptions
            {
                NumPredict = maxTokens,
                NumCtx = _numCtx
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureOllamaSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // Like /api/generate, /api/chat streams newline-delimited JSON — one object
        // per line — but each carries a `message` (content fragment and/or the
        // assembled tool_calls) rather than a flat `response` string.
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            OllamaChatResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk?.Message is null)
            {
                if (chunk?.Done == true)
                {
                    break;
                }
                continue;
            }

            var content = chunk.Message.Content;
            var toolCalls = chunk.Message.ToolCalls?
                .Select(tc => new AiToolCall(tc.Function.Name, tc.Function.Arguments))
                .ToList();

            if (!string.IsNullOrEmpty(content) || toolCalls is { Count: > 0 })
            {
                yield return new AiChatDelta(content, toolCalls);
            }

            if (chunk.Done)
            {
                break;
            }
        }
    }

    // Ollama returns the reason for a 4xx (bad option, model without tool/think
    // support, malformed tool schema) in the response body, which
    // EnsureSuccessStatusCode discards. Surface it so the failure is diagnosable.
    private static async Task EnsureOllamaSuccessAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Ollama request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {body}");
    }

    private static OllamaChatMessage ToOllama(AiMessage m) => new()
    {
        Role = m.Role,
        Content = m.Content,
        ToolName = m.ToolName,
        ToolCalls = m.ToolCalls?.Select(tc => new OllamaToolCall
        {
            Function = new OllamaToolCallFunction
            {
                Name = tc.Name,
                Arguments = tc.Arguments
            }
        }).ToList()
    };
}