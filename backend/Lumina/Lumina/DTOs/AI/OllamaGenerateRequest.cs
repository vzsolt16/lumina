using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumina.DTOs.AI;

public class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("think")]
    public bool Think { get; set; }

    [JsonPropertyName("format")]
    public JsonElement? Format { get; set; }

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; set; }
}

public class OllamaOptions
{
    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; set; }

    // Total context window (prompt + generation). Ollama defaults this to 4096,
    // which the injected document blows past; set it so requests aren't rejected.
    [JsonPropertyName("num_ctx")]
    public int? NumCtx { get; set; }
}