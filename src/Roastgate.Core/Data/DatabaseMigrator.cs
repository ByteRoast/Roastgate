using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Roastgate.Core.Data;

public static class DatabaseMigrator
{
    /// <summary>
    /// Creates the database if needed, applies pending EF Core migrations and switches SQLite to WAL mode.
    /// </summary>
    public static async Task MigrateRoastgateDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        IDbContextFactory<RoastgateDbContext> factory = services
            .GetRequiredService<IDbContextFactory<RoastgateDbContext>>();

        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        await database.Database.MigrateAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
    }
}
