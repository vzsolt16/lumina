namespace Lumina.Services.AI;

public sealed record GeminiModelDefinition(
    string Id,
    string Name,
    string ApiModel,
    string? Description = null);

public interface IGeminiModelCatalog
{
    string DefaultModelId { get; }
    IReadOnlyList<GeminiModelDefinition> Models { get; }
    GeminiModelDefinition Resolve(string? modelId);
}

public sealed class GeminiModelCatalog : IGeminiModelCatalog
{
    private static readonly IReadOnlyList<GeminiModelDefinition> AvailableModels =
    [
        new("gemini-flash", "Gemini 3.8 Flash", "gemini-3.8-flash",
            "Recommended general-purpose model"),
        new("gemini-3-flash-preview", "Gemini 3 Flash (Preview)", "gemini-3-flash-preview",
            "Preview model for advanced reasoning"),
        new("gemini-flash-lite", "Gemini 3.1 Flash-Lite", "gemini-3.1-flash-lite",
            "Fast, lightweight model")
    ];

    public GeminiModelCatalog(IConfiguration configuration)
    {
        DefaultModelId = configuration["Gemini:DefaultModel"] ?? "gemini-flash";
        if (AvailableModels.All(model => model.Id != DefaultModelId))
        {
            throw new InvalidOperationException(
                $"Gemini:DefaultModel '{DefaultModelId}' is not in the configured model list.");
        }
    }

    public string DefaultModelId { get; }
    public IReadOnlyList<GeminiModelDefinition> Models => AvailableModels;

    public GeminiModelDefinition Resolve(string? modelId)
    {
        var resolvedId = string.IsNullOrWhiteSpace(modelId) ? DefaultModelId : modelId;
        return AvailableModels.FirstOrDefault(model => model.Id == resolvedId)
            ?? throw new KeyNotFoundException($"Gemini model '{resolvedId}' is not available.");
    }
}
