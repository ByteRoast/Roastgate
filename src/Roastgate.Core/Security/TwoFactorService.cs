using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;
using Roastgate.Core.Data.Stores;

namespace Roastgate.Core.Security;

public interface ITwoFactorService
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A fresh secret. Nothing is stored until <see cref="ConfirmEnrollmentAsync"/> receives a valid code for it.
    /// </summary>
    TwoFactorEnrollment BeginEnrollment(string accountName);

    /// <summary>
    /// Turns two-factor authentication on if <paramref name="code"/> is valid for <paramref name="secret"/>
    /// (which proves that the app was set up correctly). Returns the recovery codes, shown once, or null when the
    /// code is wrong.
    /// </summary>
    Task<IReadOnlyList<string>?> ConfirmEnrollmentAsync(
        string secret,
        string code,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks a sign-in code: the current one-time code (usable once) or a recovery code (consumed).
    /// </summary>
    Task<bool> VerifyAsync(string input, CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces all recovery codes; the old ones stop working. Returns the new ones, shown once.</summary>
    Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(CancellationToken cancellationToken = default);

    Task<int> RemainingRecoveryCodesAsync(CancellationToken cancellationToken = default);
}

public sealed class TwoFactorService(
    IAdminAccountStore store,
    ISessionStamp sessionStamp,
    TimeProvider timeProvider
) : ITwoFactorService
{
    public const string Issuer = AppInfo.AppName;

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        AdminAccount? account = await store.GetAsync(cancellationToken);

        return account?.TotpSecret is not null;
    }

    public TwoFactorEnrollment BeginEnrollment(string accountName)
    {
        string secret = Totp.GenerateSecret();

        return new TwoFactorEnrollment(secret, Totp.ProvisioningUri(Issuer, accountName, secret));
    }

    public async Task<IReadOnlyList<string>?> ConfirmEnrollmentAsync(
        string secret,
        string code,
        CancellationToken cancellationToken = default
    )
    {
        if (
            await IsEnabledAsync(cancellationToken)
            || !Totp.TryVerify(secret, code, timeProvider.GetUtcNow(), null, out long step)
        )
        {
            return null;
        }

        IReadOnlyList<string> codes = RecoveryCodes.Generate();

        await store.EnableTotpAsync(secret, codes.Select(HashOf).ToList(), step, cancellationToken);

        sessionStamp.Invalidate();

        return codes;
    }

    public async Task<bool> VerifyAsync(string input, CancellationToken cancellationToken = default)
    {
        AdminAccount? account = await store.GetAsync(cancellationToken);

        if (account?.TotpSecret is not { } secret)
        {
            return false;
        }

        string? recovery = RecoveryCodes.Normalize(input);

        if (recovery is not null)
        {
            return await store.TryConsumeRecoveryCodeAsync(RecoveryCodes.Hash(recovery), cancellationToken);
        }

        if (!Totp.TryVerify(secret, input, timeProvider.GetUtcNow(), account.TotpLastStep, out long step))
        {
            return false;
        }

        // The last used step is advanced in the database in one statement: of two simultaneous logins with the same code, only one wins.
        return await store.TryAdvanceTotpStepAsync(step, cancellationToken);
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        await store.DisableTotpAsync(cancellationToken);

        sessionStamp.Invalidate();
    }

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> codes = RecoveryCodes.Generate();

        await store.ReplaceRecoveryCodesAsync(codes.Select(HashOf).ToList(), cancellationToken);

        return codes;
    }

    public async Task<int> RemainingRecoveryCodesAsync(CancellationToken cancellationToken = default)
    {
        AdminAccount? account = await store.GetAsync(cancellationToken);

        return AdminAccountStore.ParseHashes(account?.RecoveryCodeHashes).Count;
    }

    private static string HashOf(string code)
    {
        return RecoveryCodes.Hash(
            RecoveryCodes.Normalize(code)
            ?? throw new InvalidOperationException("A generated recovery code must be valid.")
        );
    }
}
