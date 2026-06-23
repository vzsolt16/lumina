using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
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
            Model = "qwen3:4b",
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

        // qwen3 is a reasoning model: it emits its chain of thought before the
        // answer, terminated by a </think> tag. The opening <think> is part of the
        // chat template, so it usually never appears in the response — only the
        // closing tag does. So we buffer the response until </think>, discard
        // everything up to and including it, then stream the answer normally. If
        // </think> never arrives, the response carried no thinking and we flush
        // the buffer as-is.
        var buffer = new StringBuilder();
        var passedThink = false;
        string? doneReason = null;

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
                // A single malformed/partial line shouldn't abort the whole stream.
                continue;
            }

            if (chunk is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(chunk.Response))
            {
                if (passedThink)
                {
                    yield return chunk.Response;
                }
                else
                {
                    buffer.Append(chunk.Response);

                    // Searching the whole buffer each time reassembles a </think>
                    // split across chunk boundaries; the buffer is only as large
                    // as the (short-lived) thinking section.
                    var buffered = buffer.ToString();
                    var close = buffered.IndexOf(ThinkClose, StringComparison.Ordinal);
                    if (close >= 0)
                    {
                        var after = buffered[(close + ThinkClose.Length)..].TrimStart();
                        buffer.Clear();
                        passedThink = true;
                        if (after.Length > 0)
                        {
                            yield return after;
                        }
                    }
                }
            }

            if (chunk.Done)
            {
                doneReason = chunk.DoneReason;
                break;
            }
        }

        // We never saw </think>. Because this model always opens a thinking section,
        // that means generation stopped while still inside it — almost always
        // because it hit the token cap. Surface that rather than dumping the raw
        // reasoning as if it were the answer (which is what a naive flush would do).
        if (!passedThink)
        {
            if (doneReason == "length")
            {
                throw new InvalidOperationException(
                    "The model ran out of room while reasoning and didn't reach an answer. " +
                    "Try asking a more specific question.");
            }

            // Natural stop with no thinking section at all (e.g. thinking genuinely
            // disabled): treat the buffer as the answer.
            if (buffer.Length > 0)
            {
                yield return buffer.ToString().Trim();
            }
        }
    }

    private const string ThinkOpen = "<think>";
    private const string ThinkClose = "</think>";
}