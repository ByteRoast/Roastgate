using Microsoft.EntityFrameworkCore;

namespace Roastgate.Core.Data;

/// <summary>A copy of a config file taken just before Roastgate changed or deleted it.</summary>
public sealed class ConfigBackup
{
    public long Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The <see cref="Roastgate.Core.Nginx.ConfigArea"/> name.</summary>
    public required string Area { get; set; }

    public required string Name { get; set; }

    /// <summary>Why the backup was taken (for example "save", "delete", "restore").</summary>
    public required string Reason { get; set; }

    public required string Content { get; set; }
}

public interface IConfigBackupStore
{
    Task<long> AddAsync(string area, string name, string reason, string content, CancellationToken cancellationToken = default);

    /// <summary>Newest first. Filters are optional.</summary>
    Task<IReadOnlyList<ConfigBackup>> ListAsync(string? area, string? name, int count, CancellationToken cancellationToken = default);

    Task<ConfigBackup?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Removes a backup, used when the change it was taken for was refused and undone (it would only duplicate the current file).</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class ConfigBackupStore(IDbContextFactory<RoastgateDbContext> factory) : IConfigBackupStore
{
    /// <summary>Only the most recent backups of each file are kept.</summary>
    public const int KeptPerFile = 30;

    public async Task<long> AddAsync(string area, string name, string reason, string content, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        ConfigBackup backup = new()
        {
            CreatedAt = DateTimeOffset.UtcNow,
            Area = area,
            Name = name,
            Reason = reason,
            Content = content,
        };
        database.ConfigBackups.Add(backup);
        await database.SaveChangesAsync(cancellationToken);

        List<long> obsolete = await database.ConfigBackups
            .Where(item => item.Area == area && item.Name == name)
            .OrderByDescending(item => item.Id)
            .Skip(KeptPerFile)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (obsolete.Count > 0)
        {
            await database.ConfigBackups.Where(item => obsolete.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        return backup.Id;
    }

    public async Task<IReadOnlyList<ConfigBackup>> ListAsync(string? area, string? name, int count, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        IQueryable<ConfigBackup> query = database.ConfigBackups.AsNoTracking();
        if (area is not null)
        {
            query = query.Where(item => item.Area == area);
        }

        if (name is not null)
        {
            query = query.Where(item => item.Name == name);
        }

        return await query.OrderByDescending(item => item.Id).Take(count).ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        await database.ConfigBackups.Where(item => item.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<ConfigBackup?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        return await database.ConfigBackups.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }
}
