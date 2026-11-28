using System.Net;
using System.Text.Json;

namespace Lumina.Tests;

public sealed class HealthEndpointTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task LiveEndpointDoesNotDependOnExternalServices()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReadinessResponseDoesNotExposeDependencyDetails()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable });
        using var json = JsonDocument.Parse(body);
        var status = json.RootElement.GetProperty("status").GetString();
        Assert.Contains(status, new[] { "Healthy", "Unhealthy" });
        Assert.Equal(status == "Healthy" ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("qdrant", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", body, StringComparison.OrdinalIgnoreCase);
    }
}
