namespace Lumina.DTOs.AI;

public class OllamaGenerateRequest
{
    public string Model { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public bool Stream { get; set; }

    public OllamaOptions? Options { get; set; }
}

public class OllamaOptions
{
    public int? NumPredict { get; set; }
}