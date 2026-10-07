using Microsoft.EntityFrameworkCore;

namespace Roastgate.Core.Data;

public interface IAdminAccountStore
{
    Task<AdminAccount?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the account. Returns false (and changes nothing) if one already exists.</summary>
    Task<bool> CreateAsync(string username, string passwordHash, CancellationToken cancellationToken = default);

    Task UpdatePasswordHashAsync(string passwordHash, CancellationToken cancellationToken = default);
}

public sealed class AdminAccountStore(IDbContextFactory<RoastgateDbContext> factory) : IAdminAccountStore
{
    public async Task<AdminAccount?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        return await database.AdminAccounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> CreateAsync(string username, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        if (await database.AdminAccounts.AnyAsync(cancellationToken))
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        database.AdminAccounts.Add(new AdminAccount
        {
            Username = username,
            PasswordHash = passwordHash,
            CreatedAt = now,
            UpdatedAt = now,
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost a race with another request: the primary key (id = 1) already exists.
            return false;
        }
    }

    public async Task UpdatePasswordHashAsync(string passwordHash, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await database.AdminAccounts.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(account => account.PasswordHash, passwordHash)
                .SetProperty(account => account.UpdatedAt, now),
            cancellationToken);
    }
}
