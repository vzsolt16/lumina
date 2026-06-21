using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Lumina.Services.Ai;

public static class AiJsonParser
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    public static T Parse<T>(string response, ILogger logger)
    {
        var text = Regex.Replace(response, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();

        // Ollama is called with a JSON schema ('format'), so the response is
        // normally already pure JSON — try the whole string first. Only if that
        // fails do we fall back to slicing out the outermost { ... }, which
        // tolerates the rare case of stray prose around the JSON.
        if (TryDeserialize<T>(text, out var result))
        {
            return result;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start && TryDeserialize<T>(text[start..(end + 1)], out result))
        {
            return result;
        }

        logger.LogError("Failed to parse AI response as {Type}: {Response}", typeof(T).Name, response);
        throw new InvalidOperationException($"Could not parse AI response as {typeof(T).Name}.");
    }

    private static bool TryDeserialize<T>(string json, out T result)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(json, CaseInsensitive);
            if (parsed is not null)
            {
                result = parsed;
                return true;
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — let the caller try the next strategy.
        }

        result = default!;
        return false;
    }
}
