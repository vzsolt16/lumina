namespace Lumina.DTOs.AI;

public class OllamaGenerateResponse
{
    public string Response { get; set; } = string.Empty;
    
    public bool Done { get; set; }
}