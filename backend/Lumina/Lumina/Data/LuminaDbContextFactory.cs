using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lumina.Data;

/// <summary>Lets EF tooling generate migrations without starting the web host or its background workers.</summary>
public sealed class LuminaDbContextFactory : IDesignTimeDbContextFactory<LuminaDbContext>
{
    public LuminaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LuminaDbContext>()
            .UseSqlite("Data Source=lumina.design.db")
            .Options;
        return new LuminaDbContext(options);
    }
}
