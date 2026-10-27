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
