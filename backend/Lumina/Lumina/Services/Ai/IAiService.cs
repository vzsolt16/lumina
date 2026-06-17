namespace Lumina.Services.AI;

public interface IAiService
{
    Task<string> GenerateAsync(string prompt);
}