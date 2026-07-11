using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumina.DTOs.AI;

// The /api/chat endpoint (as opposed to /api/generate) — the only Ollama endpoint
// that accepts a `tools` array and returns `tool_calls`.
public class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("think")]
    public bool Think { get; set; }

    [JsonPropertyName("messages")]
    public List<OllamaChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("tools")]
    public List<OllamaTool>? Tools { get; set; }

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; set; }
}

public class OllamaChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("tool_calls")]
    public List<OllamaToolCall>? ToolCalls { get; set; }

    // Set on a role:"tool" message so the model knows which tool produced the result.
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }
}

public class OllamaTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OllamaFunction Function { get; set; } = new();
}

public class OllamaFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    // A JSON Schema object describing the tool's arguments.
    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }
}

public class OllamaToolCall
{
    [JsonPropertyName("function")]
    public OllamaToolCallFunction Function { get; set; } = new();
}

public class OllamaToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    // Ollama returns arguments as a JSON object, not a JSON-encoded string.
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}
