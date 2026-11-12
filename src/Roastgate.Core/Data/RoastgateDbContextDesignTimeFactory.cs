using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Roastgate.Core.Data;

/// <summary>Lets <c>dotnet ef</c> create the context without starting the application.</summary>
public sealed class RoastgateDbContextDesignTimeFactory : IDesignTimeDbContextFactory<RoastgateDbContext>
{
    public RoastgateDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<RoastgateDbContext> builder = new();
        builder.UseSqlite("Data Source=design-time.db");

        return new RoastgateDbContext(builder.Options);
    }
}
