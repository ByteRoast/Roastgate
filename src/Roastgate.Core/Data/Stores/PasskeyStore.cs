using Microsoft.EntityFrameworkCore;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;

namespace Roastgate.Core.Data.Stores;

public sealed class PasskeyStore(IDbContextFactory<RoastgateDbContext> factory) : IPasskeyStore
{
    public async Task<IReadOnlyList<PasskeyCredential>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        
        return await database.Passkeys.AsNoTracking().OrderBy(passkey => passkey.Id).ToListAsync(cancellationToken);
    }

    public async Task<PasskeyCredential?> FindAsync(byte[] credentialId, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        
        return await database.Passkeys.AsNoTracking()
            .FirstOrDefaultAsync(passkey => passkey.CredentialId == credentialId, cancellationToken);
    }

    public async Task<bool> AddAsync(PasskeyCredential passkey, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        
        database.Passkeys.Add(passkey);
        
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        
            return true;
        }
        catch (DbUpdateException)
        {
            // The unique index on the credential id refused it.
            return false;
        }
    }

    public async Task<bool> TryRecordUseAsync(int id, long expectedCounter, long newCounter, DateTimeOffset usedAt,
        CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        
        int updated = await database.Passkeys
            .Where(passkey => passkey.Id == id && passkey.SignatureCounter == expectedCounter)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(passkey => passkey.SignatureCounter, newCounter)
                    .SetProperty(passkey => passkey.LastUsedAt, (DateTimeOffset?)usedAt),
                cancellationToken);
        
        return updated > 0;
    }

    public async Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        
        return await database.Passkeys.Where(passkey => passkey.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        await using RoastgateDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        await database.Passkeys.ExecuteDeleteAsync(cancellationToken);
    }
}
