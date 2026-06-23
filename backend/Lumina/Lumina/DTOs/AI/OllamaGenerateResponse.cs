using System.Text.Json.Serialization;

namespace Lumina.DTOs.AI;

public class OllamaGenerateResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    // Why generation stopped: "stop" (natural end), "length" (hit the token
    // cap → output likely truncated), etc. Snake_case in Ollama's JSON.
    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; set; }
}