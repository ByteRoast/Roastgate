using Microsoft.EntityFrameworkCore;

namespace Roastgate.Core.Data;

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class SettingsStore(IDbContextFactory<RoastgateDbContext> factory) : ISettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        Setting? setting = await database.Settings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        Setting? setting = await database.Settings.SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (setting is null)
        {
            database.Settings.Add(new Setting { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        int deleted = await database.Settings.Where(item => item.Key == key).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }
}
