using System.Text.Json;
using Lumina.Data;
using Lumina.Models;

namespace Lumina.Services.Rag;

public sealed class RagTelemetryService(IServiceScopeFactory scopes, ILogger<RagTelemetryService> logger)
{
    public async Task PersistAsync(RagExecution execution)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();
            db.RagExecutions.Add(execution);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist RAG execution {ExecutionId}", execution.Id);
        }
    }

    public static string? SerializeDetails(object details) => JsonSerializer.Serialize(details, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
