using Roastgate.Core.Data.Models;

namespace Roastgate.Core.Data.Interfaces;

public interface IPasskeyStore
{
    Task<IReadOnlyList<PasskeyCredential>> ListAsync(CancellationToken cancellationToken = default);

    Task<PasskeyCredential?> FindAsync(byte[] credentialId, CancellationToken cancellationToken = default);

    /// <summary>Stores the passkey. Returns false (and changes nothing) when that credential is already registered.</summary>
    Task<bool> AddAsync(PasskeyCredential passkey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful sign-in. The counter only moves when it still has the value that was verified, so two simultaneous
    /// sign-ins cannot both be accepted on the same counter. Returns false when it changed in between.
    /// </summary>
    Task<bool> TryRecordUseAsync(int id, long expectedCounter, long newCounter, DateTimeOffset usedAt,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default);

    Task RemoveAllAsync(CancellationToken cancellationToken = default);
}
