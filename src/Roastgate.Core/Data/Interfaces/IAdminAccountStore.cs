using Roastgate.Core.Data.Models;

namespace Roastgate.Core.Data.Interfaces;

public interface IAdminAccountStore
{
    Task<AdminAccount?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the account. Returns false (and changes nothing) if one already exists.</summary>
    Task<bool> CreateAsync(string username, string passwordHash, CancellationToken cancellationToken = default);

    /// <param name="rotateStamp">True when the password really changed (every open session ends); false when only the hash was upgraded.</param>
    Task UpdatePasswordHashAsync(string passwordHash, bool rotateStamp = true,
        CancellationToken cancellationToken = default);

    /// <summary>Turns two-factor authentication on. Every open session ends (the security stamp is renewed).</summary>
    Task EnableTotpAsync(string secret, IReadOnlyList<string> recoveryCodeHashes, long acceptedStep,
        CancellationToken cancellationToken = default);

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

    /// <summary>Renews the security stamp: every open session ends.</summary>
    Task RotateSecurityStampAsync(CancellationToken cancellationToken = default);
}
