using Lumina.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lumina.Health;

public sealed class SqliteReadinessCheck(LuminaDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connected = await database.Database.CanConnectAsync(cancellationToken);
        return connected ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }
}

public sealed class QdrantReadinessCheck(IHttpClientFactory clients) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await clients.CreateClient("QdrantHealth")
                .GetAsync("healthz", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (HttpRequestException)
        {
            return HealthCheckResult.Unhealthy();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
