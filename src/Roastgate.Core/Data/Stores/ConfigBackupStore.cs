using Microsoft.EntityFrameworkCore;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;

namespace Roastgate.Core.Data.Stores;

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
