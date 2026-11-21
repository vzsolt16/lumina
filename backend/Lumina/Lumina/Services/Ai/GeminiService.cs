using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumina.Services.AI;

public sealed class GeminiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IGeminiModelCatalog _catalog;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(
        HttpClient httpClient,
        IConfiguration configuration,
        IGeminiModelCatalog catalog,
        ILogger<GeminiService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<int?> CountInputTokensAsync(IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools,
        CancellationToken cancellationToken = default, string? modelId = null)
    {
        // countTokens accepts a GenerateContentRequest inside this wrapper. Its
        // top-level request only accepts `contents`, so sending the generation
        // fields directly is rejected as an invalid payload.
        var model = _catalog.Resolve(modelId);
        var body = new JsonObject
        {
            ["generateContentRequest"] = CreateRequestBody(messages, tools, 4000)
        };
        body["generateContentRequest"]!["model"] = $"models/{model.ApiModel}";
        using var response = await SendAsync("countTokens", modelId, body, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Gemini returned an empty token-count response.");
        return json["totalTokens"]?.GetValue<int>();
    }

    public async Task<string> GenerateAsync(string prompt, JsonElement? schema = null, int maxTokens = 2000,
        string? modelId = null)
    {
        var body = CreateRequestBody(
            [new AiMessage("user", prompt)],
            [],
            maxTokens,
            schema);
        using var response = await SendAsync("generateContent", modelId, body, CancellationToken.None);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>()
            ?? throw new InvalidOperationException("Gemini returned an empty response.");
        return ExtractText(json) ?? throw new InvalidOperationException("Gemini returned no text.");
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        string prompt,
        int maxTokens = 2000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        string? modelId = null)
    {
        var body = CreateRequestBody([new AiMessage("user", prompt)], [], maxTokens);
        await foreach (var json in StreamAsync(modelId, body, cancellationToken))
        {
            var text = ExtractText(json);
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    public async IAsyncEnumerable<AiChatDelta> ChatStreamAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiTool> tools,
        int maxTokens = 4000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        string? modelId = null)
    {
        var body = CreateRequestBody(messages, tools, maxTokens);
        await foreach (var json in StreamAsync(modelId, body, cancellationToken))
        {
            var parts = json["candidates"]?[0]?["content"]?["parts"]?.AsArray();
            var usageJson = json["usageMetadata"];
            var usage = usageJson is null ? null : new AiUsageMetadata(
                usageJson["promptTokenCount"]?.GetValue<int>(),
                usageJson["candidatesTokenCount"]?.GetValue<int>(),
                usageJson["totalTokenCount"]?.GetValue<int>());
            if (parts is null) { if (usage is not null) yield return new AiChatDelta(null, null, usage); continue; }

            var text = string.Join(string.Empty,
                parts.Select(part => part?["text"]?.GetValue<string>() ?? string.Empty));
            var calls = parts
                .Where(part => part?["functionCall"] is not null)
                .Select(part =>
                {
                    var call = part!["functionCall"]!;
                    return new AiToolCall(
                        call["name"]!.GetValue<string>(),
                        call["args"]?.Deserialize<JsonElement>() ?? EmptyObject(),
                        part["thoughtSignature"]?.GetValue<string>());
                })
                .ToList();

            if (text.Length > 0 || calls.Count > 0 || usage is not null)
            {
                yield return new AiChatDelta(text, calls.Count == 0 ? null : calls, usage);
            }
        }
    }

    private async IAsyncEnumerable<JsonObject> StreamAsync(
        string? modelId,
        JsonObject body,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await SendAsync("streamGenerateContent", modelId, body, cancellationToken, stream: true);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = line[5..].Trim();
            if (payload.Length == 0 || payload == "[DONE]")
            {
                continue;
            }

            var json = JsonNode.Parse(payload)?.AsObject();
            if (json is not null)
            {
                yield return json;
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        string operation,
        string? modelId,
        JsonObject body,
        CancellationToken cancellationToken,
        bool stream = false)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("Gemini:ApiKey is not configured.");
        }
        var model = _catalog.Resolve(modelId);
        var suffix = stream ? "&alt=sse" : string.Empty;
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1beta/models/{model.ApiModel}:{operation}?key={Uri.EscapeDataString(_apiKey)}{suffix}")
            {
                Content = JsonContent.Create(body)
            };
            var response = await _httpClient.SendAsync(
                request,
                stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            var transient = response.StatusCode == System.Net.HttpStatusCode.RequestTimeout
                || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500;
            if (!transient || attempt == maxAttempts)
            {
                _logger.LogError(
                    "Gemini request failed after {AttemptCount} attempt(s): {StatusCode} {Detail}",
                    attempt, (int)response.StatusCode, detail);
                response.Dispose();
                throw new HttpRequestException(
                    $"Gemini request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}",
                    null,
                    response.StatusCode);
            }

            _logger.LogWarning(
                "Transient Gemini failure on attempt {Attempt}: {StatusCode} {Detail}",
                attempt, (int)response.StatusCode, detail);
            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
        }

        throw new InvalidOperationException("Gemini request retry loop exited unexpectedly.");
    }

    private static JsonObject CreateRequestBody(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiTool> tools,
        int maxTokens,
        JsonElement? schema = null)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(messages
                .Where(message => message.Role != "system")
                .Select(ToContent)
                .ToArray()),
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = maxTokens
            }
        };

        var system = messages.FirstOrDefault(message => message.Role == "system");
        if (system is not null)
        {
            body["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = system.Content })
            };
        }

        var config = body["generationConfig"]!.AsObject();
        if (schema is not null)
        {
            config["responseMimeType"] = "application/json";
            config["responseSchema"] = JsonNode.Parse(schema.Value.GetRawText());
        }

        if (tools.Count > 0)
        {
            body["tools"] = new JsonArray(new JsonObject
            {
                ["functionDeclarations"] = new JsonArray(tools.Select(tool => new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parametersJsonSchema"] = JsonNode.Parse(tool.Parameters.GetRawText())
                }).ToArray())
            });
        }

        return body;
    }

    private static JsonObject ToContent(AiMessage message)
    {
        var role = message.Role == "assistant" ? "model" : "user";
        var parts = new JsonArray();
        if (!string.IsNullOrEmpty(message.Content))
        {
            parts.Add(new JsonObject { ["text"] = message.Content });
        }

        if (message.ToolCalls is not null)
        {
            foreach (var call in message.ToolCalls)
            {
                var functionCallPart = new JsonObject
                {
                    ["functionCall"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        ["args"] = JsonNode.Parse(call.Arguments.GetRawText())
                    }
                };
                if (!string.IsNullOrEmpty(call.ThoughtSignature))
                {
                    functionCallPart["thoughtSignature"] = call.ThoughtSignature;
                }
                parts.Add(functionCallPart);
            }
        }

        if (message.Role == "tool")
        {
            parts.Clear();
            parts.Add(new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = message.ToolName,
                    ["response"] = new JsonObject { ["result"] = message.Content }
                }
            });
        }

        return new JsonObject { ["role"] = role, ["parts"] = parts };
    }

    private static string? ExtractText(JsonObject json)
    {
        return json["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
    }

    private static JsonElement EmptyObject() =>
        JsonSerializer.Deserialize<JsonElement>("{}");
}
