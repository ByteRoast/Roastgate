using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Roastgate.Core.Data;

public interface IAdminAccountStore
{
    Task<AdminAccount?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the account. Returns false (and changes nothing) if one already exists.</summary>
    Task<bool> CreateAsync(string username, string passwordHash, CancellationToken cancellationToken = default);

    /// <param name="rotateStamp">True when the password really changed (every open session ends); false when only the hash was upgraded.</param>
    Task UpdatePasswordHashAsync(string passwordHash, bool rotateStamp = true, CancellationToken cancellationToken = default);

    /// <summary>Turns two-factor authentication on. Every open session ends (the security stamp is renewed).</summary>
    Task EnableTotpAsync(string secret, IReadOnlyList<string> recoveryCodeHashes, long acceptedStep, CancellationToken cancellationToken = default);

    /// <summary>Turns it off and forgets the secret and the recovery codes. Every open session ends.</summary>
    Task DisableTotpAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the code of <paramref name="step"/> was used. Returns false when that step, or a later one, was already used:
    /// the check and the update are one statement, so two simultaneous logins cannot both use the same code.
    /// </summary>
    Task<bool> TryAdvanceTotpStepAsync(long step, CancellationToken cancellationToken = default);

    /// <summary>Removes the recovery code with that hash. Returns false when it is not (or no longer) there.</summary>
    Task<bool> TryConsumeRecoveryCodeAsync(string hash, CancellationToken cancellationToken = default);

    Task ReplaceRecoveryCodesAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default);
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
            SecurityStamp = NewStamp(),
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

    public async Task UpdatePasswordHashAsync(string passwordHash, bool rotateStamp = true, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (rotateStamp)
        {
            string stamp = NewStamp();
            await database.AdminAccounts.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(account => account.PasswordHash, passwordHash)
                    .SetProperty(account => account.SecurityStamp, stamp)
                    .SetProperty(account => account.UpdatedAt, now),
                cancellationToken);
            return;
        }

        await database.AdminAccounts.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(account => account.PasswordHash, passwordHash)
                .SetProperty(account => account.UpdatedAt, now),
            cancellationToken);
    }

    public async Task EnableTotpAsync(string secret, IReadOnlyList<string> recoveryCodeHashes, long acceptedStep, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        string stamp = NewStamp();
        string hashes = JsonSerializer.Serialize(recoveryCodeHashes);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await database.AdminAccounts.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(account => account.TotpSecret, secret)
                .SetProperty(account => account.TotpLastStep, (long?)acceptedStep)
                .SetProperty(account => account.RecoveryCodeHashes, hashes)
                .SetProperty(account => account.SecurityStamp, stamp)
                .SetProperty(account => account.UpdatedAt, now),
            cancellationToken);
    }

    public async Task DisableTotpAsync(CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        string stamp = NewStamp();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await database.AdminAccounts.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(account => account.TotpSecret, (string?)null)
                .SetProperty(account => account.TotpLastStep, (long?)null)
                .SetProperty(account => account.RecoveryCodeHashes, (string?)null)
                .SetProperty(account => account.SecurityStamp, stamp)
                .SetProperty(account => account.UpdatedAt, now),
            cancellationToken);
    }

    public async Task<bool> TryAdvanceTotpStepAsync(long step, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        int updated = await database.AdminAccounts
            .Where(account => account.TotpSecret != null && (account.TotpLastStep == null || account.TotpLastStep < step))
            .ExecuteUpdateAsync(setters => setters.SetProperty(account => account.TotpLastStep, (long?)step), cancellationToken);
        return updated > 0;
    }

    public async Task<bool> TryConsumeRecoveryCodeAsync(string hash, CancellationToken cancellationToken = default)
    {
        // Compare and swap: if another login consumed a code in between, the stored value no longer matches and we read it again.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
            AdminAccount? account = await database.AdminAccounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (account?.RecoveryCodeHashes is not { } current)
            {
                return false;
            }

            List<string> remaining = ParseHashes(current);
            if (!remaining.Remove(hash))
            {
                return false;
            }

            string updatedJson = JsonSerializer.Serialize(remaining);
            int updated = await database.AdminAccounts
                .Where(item => item.RecoveryCodeHashes == current)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RecoveryCodeHashes, updatedJson), cancellationToken);
            if (updated > 0)
            {
                return true;
            }
        }

        return false;
    }

    public async Task ReplaceRecoveryCodesAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        string json = JsonSerializer.Serialize(hashes);
        await database.AdminAccounts
            .Where(account => account.TotpSecret != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(account => account.RecoveryCodeHashes, json), cancellationToken);
    }

    /// <summary>The hashes stored in the JSON column (an empty list when there is none or it cannot be read).</summary>
    public static List<string> ParseHashes(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string NewStamp()
    {
        return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).TrimEnd('=');
    }
}
