using System.Text.Json.Serialization;

namespace Lumina.DTOs.AI;

// One streamed NDJSON line from /api/chat. `message` carries the growing reply
// (content fragment and/or the assembled tool_calls); `done` marks the last line.
public class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaChatMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; set; }
}
