using System.Text.Json;

namespace Lumina.Services.AI;

// Provider-neutral chat types so callers (ChatService) never depend on Ollama's
// wire format. OllamaService maps these to/from its own DTOs.

// role: "system" | "user" | "assistant" | "tool".
// ToolCalls is set on an assistant turn that requested tools; ToolName is set on
// a "tool" turn carrying that tool's result back to the model.
public sealed record AiMessage(
    string Role,
    string Content,
    IReadOnlyList<AiToolCall>? ToolCalls = null,
    string? ToolName = null);

// A tool the model may call. Parameters is a JSON Schema object describing the
// arguments.
public sealed record AiTool(string Name, string Description, JsonElement Parameters);

// A tool call the model decided to make. Arguments is the parsed argument object.
public sealed record AiToolCall(string Name, JsonElement Arguments);

// One streamed delta from a chat turn: a content fragment, and/or the completed
// tool-call list (Ollama emits tool calls assembled, not token-by-token).
public sealed record AiChatDelta(string? Content, IReadOnlyList<AiToolCall>? ToolCalls);
