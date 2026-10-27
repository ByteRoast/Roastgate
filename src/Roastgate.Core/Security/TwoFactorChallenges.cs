using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Roastgate.Core.Security;

/// <summary>
/// Between the password and the code the user is "half signed in": a ticket stands for that state. It lives five
/// minutes, allows five wrong codes, and works once, so the second step cannot be attacked with unlimited tries.
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

        foreach (
            KeyValuePair<string, Challenge> expired in _challenges.Where((KeyValuePair<string, Challenge> pair) =>
                pair.Value.ExpiresAt <= now)
        )
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

    /// <summary>
    /// Counts a wrong code. Returns false when that was the last allowed one (the ticket is then gone).
    /// </summary>
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
