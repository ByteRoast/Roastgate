using System.Collections.Concurrent;
using System.Security.Cryptography;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;
using Roastgate.Core.Data.Stores;

namespace Roastgate.Core.Security;

/// <summary>What the user scans (the QR code carries <see cref="ProvisioningUri"/>) or types in (<see cref="Secret"/>).</summary>
public sealed record TwoFactorEnrollment(string Secret, string ProvisioningUri);

public interface ITwoFactorService
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>A fresh secret. Nothing is stored until <see cref="ConfirmEnrollmentAsync"/> receives a valid code for it.</summary>
    TwoFactorEnrollment BeginEnrollment(string accountName);

    /// <summary>
    /// Turns two-factor authentication on if <paramref name="code"/> is valid for <paramref name="secret"/> (which proves that the
    /// app was set up correctly). Returns the recovery codes, shown once, or null when the code is wrong.
    /// </summary>
    Task<IReadOnlyList<string>?> ConfirmEnrollmentAsync(string secret, string code, CancellationToken cancellationToken = default);

    /// <summary>Checks a sign-in code: the current one-time code (usable once) or a recovery code (consumed).</summary>
    Task<bool> VerifyAsync(string input, CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces all recovery codes; the old ones stop working. Returns the new ones, shown once.</summary>
    Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(CancellationToken cancellationToken = default);

    Task<int> RemainingRecoveryCodesAsync(CancellationToken cancellationToken = default);
}

public sealed class TwoFactorService(IAdminAccountStore store, ISessionStamp sessionStamp, TimeProvider timeProvider) : ITwoFactorService
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

    public async Task<IReadOnlyList<string>?> ConfirmEnrollmentAsync(string secret, string code, CancellationToken cancellationToken = default)
    {
        if (await IsEnabledAsync(cancellationToken) || !Totp.TryVerify(secret, code, timeProvider.GetUtcNow(), null, out long step))
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
        return RecoveryCodes.Hash(RecoveryCodes.Normalize(code) ?? throw new InvalidOperationException("A generated recovery code must be valid."));
    }
}

/// <summary>
/// Between the password and the code the user is "half signed in": a ticket stands for that state. It lives five minutes, allows five
/// wrong codes, and works once, so the second step cannot be attacked with unlimited tries.
/// </summary>
public sealed class TwoFactorChallenges(TimeProvider timeProvider)
{
    public const int MaximumAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private sealed class Challenge(string username, DateTimeOffset expiresAt)
    {
        public string Username { get; } = username;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        // A field, not a property: it is incremented with Interlocked.
        public int FailureCount;
    }

    private readonly ConcurrentDictionary<string, Challenge> _challenges = new();

    public string Issue(string username)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        
        foreach (KeyValuePair<string, Challenge> expired in _challenges.Where((KeyValuePair<string, Challenge> pair) => pair.Value.ExpiresAt <= now))
        {
            _challenges.TryRemove(expired.Key, out _);
        }

        string ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        
        _challenges[ticket] = new Challenge(username, now + Lifetime);
        
        return ticket;
    }

    /// <summary>The user the ticket was issued for, or null when it is unknown, expired or used up.</summary>
    public string? Peek(string ticket)
    {
        if (_challenges.TryGetValue(ticket, out Challenge? challenge) && challenge.ExpiresAt > timeProvider.GetUtcNow())
        {
            return challenge.Username;
        }

        return null;
    }

    /// <summary>Counts a wrong code. Returns false when that was the last allowed one (the ticket is then gone).</summary>
    public bool RecordFailure(string ticket)
    {
        if (!_challenges.TryGetValue(ticket, out Challenge? challenge))
        {
            return false;
        }

        if (Interlocked.Increment(ref challenge.FailureCount) >= MaximumAttempts)
        {
            _challenges.TryRemove(ticket, out _);
          
            return false;
        }

        return true;
    }

    /// <summary>Uses the ticket up (after a successful code).</summary>
    public void Complete(string ticket)
    {
        _challenges.TryRemove(ticket, out _);
    }
}
