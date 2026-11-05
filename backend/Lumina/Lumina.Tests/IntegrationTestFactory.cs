using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lumina.Data;
using Lumina.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Lumina.Tests;

public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    private DbConnection? _connection;
    private string? _storageRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "lumina-tests", Guid.NewGuid().ToString("N"));
        builder.UseSetting("Storage:LocalRootPath", _storageRoot);
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(
                d => d.ServiceType == typeof(DbContextOptions<LuminaDbContext>) ||
                     d.ServiceType == typeof(DbConnection)).ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddSingleton<DbConnection>(_connection);
            services.AddDbContext<LuminaDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();
            // The application startup applies migrations. Use the same migration
            // path here; EnsureCreated creates the schema without migration
            // history, so Program.cs would try to create the tables again.
            db.Database.Migrate();

            var user = new ApplicationUser { Id = Guid.Parse(TestAuthHandler.TestUserId), Email = "test@test.com", UserName = "test@test.com" };
            db.Users.Add(user);
            db.SaveChanges();

            // Mock Authentication
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Dispose();
        if (_storageRoot is not null && Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public static readonly string TestUserId = Guid.NewGuid().ToString();

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, TestUserId) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
