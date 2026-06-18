using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Lumina.Services.Ai;

public static class AiJsonParser
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    public static T Parse<T>(string response, ILogger logger)
    {
        try
        {
            var text = Regex.Replace(response, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            var json = start >= 0 && end > start ? text[start..(end + 1)] : text;

            return JsonSerializer.Deserialize<T>(json, CaseInsensitive)
                   ?? throw new InvalidOperationException($"Deserializing AI response as {typeof(T).Name} returned null.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse AI response: {Response}", response);
            throw;
        }
    }
}
