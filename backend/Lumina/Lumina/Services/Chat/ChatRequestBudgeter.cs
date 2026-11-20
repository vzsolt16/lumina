using System.Text.Json;
using Lumina.Services.AI;

namespace Lumina.Services.Chat;

/// <summary>Owns request admission and safe whole-message reduction. 
/// The fallback estimator is deliberately conservative: UTF-8 bytes / 3,
///  with extra allowance for message framing and serialized tool schemas.</summary>
public sealed class ChatRequestBudgeter(IConfiguration configuration, IGeminiModelCatalog catalog)
{
    public int AvailableInputTokens(string modelId)
    {
        var model = catalog.Resolve(modelId);
        var contextLimit = configuration.GetValue($"Gemini:Models:{model.Id}:ContextTokens", model.ContextTokens);
        return Math.Max(0, contextLimit
            - configuration.GetValue("Chat:MaxOutputTokens", 4000)
            - configuration.GetValue("Chat:ContextSafetyMarginTokens", 2048));
    }

    public static int EstimateTokens(IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools)
    {
        var bytes = messages.Sum(m => System.Text.Encoding.UTF8.GetByteCount(m.Content)
            + (m.ToolCalls is null ? 0 : JsonSerializer.SerializeToUtf8Bytes(m.ToolCalls).Length));
        bytes += tools.Sum(t => JsonSerializer.SerializeToUtf8Bytes(new { t.Name, t.Description, t.Parameters }).Length);
        return (bytes + 2) / 3 + messages.Count * 8 + tools.Count * 12;
    }

    public bool Fit(List<AiMessage> messages, IReadOnlyList<AiTool> tools, int maxInputTokens)
    {
        while (EstimateTokens(messages, tools) > maxInputTokens)
        {
            // Stored history is user/assistant pairs. Remove the oldest complete pair only.
            var firstUser = messages.FindIndex(m => m.Purpose == AiMessagePurpose.History && m.Role == "user");
            if (firstUser >= 0 && firstUser + 1 < messages.Count
                && messages[firstUser + 1].Purpose == AiMessagePurpose.History
                && messages[firstUser + 1].Role == "assistant")
            { messages.RemoveRange(firstUser, 2); continue; }
            var evidence = messages.FindLastIndex(m => m.Purpose == AiMessagePurpose.Evidence);
            if (evidence >= 0)
            {
                messages.RemoveAt(evidence);
                var systemIndex = messages.FindIndex(m => m.Role == "system");
                if (systemIndex >= 0 && !messages[systemIndex].Content.Contains("Evidence omitted to fit context budget", StringComparison.Ordinal))
                    messages[systemIndex] = messages[systemIndex] with { Content = messages[systemIndex].Content + "\nSome relevant document evidence was omitted to fit the model context budget. Do not imply the supplied passages are exhaustive." };
                continue;
            }
            return false;
        }
        return true;
    }
}
