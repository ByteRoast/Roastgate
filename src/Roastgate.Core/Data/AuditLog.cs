using Microsoft.EntityFrameworkCore;

namespace Roastgate.Core.Data;

public interface IAuditLog
{
    Task WriteAsync(
        string actor,
        string action,
        string? target,
        bool success,
        string? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>Most recent entries first.</summary>
    Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int count, CancellationToken cancellationToken = default);
}

public sealed class AuditLog(IDbContextFactory<RoastgateDbContext> factory) : IAuditLog
{
    public async Task WriteAsync(
        string actor,
        string action,
        string? target,
        bool success,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        database.AuditLog.Add(new AuditEntry
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Actor = actor,
            Action = action,
            Target = target,
            Success = success,
            Details = details,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);

        // Ordered by id: SQLite cannot order DateTimeOffset values in SQL, and ids grow with time.
        return await database.AuditLog
            .AsNoTracking()
            .OrderByDescending(entry => entry.Id)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}
