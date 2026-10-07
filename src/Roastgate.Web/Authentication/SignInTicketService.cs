using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Roastgate.Web.Authentication;

/// <summary>
/// Single-use, short-lived tickets. The setup wizard runs in a Blazor circuit, which cannot set cookies,
/// so it trades a ticket for a real sign-in through the <c>/sign-in/{ticket}</c> endpoint.
/// </summary>
public sealed class SignInTicketService(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, (string Username, DateTimeOffset ExpiresAt)> _tickets = new();

    public string Issue(string username)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (KeyValuePair<string, (string Username, DateTimeOffset ExpiresAt)> expired in _tickets.Where(
                     (KeyValuePair<string, (string Username, DateTimeOffset ExpiresAt)> pair) => pair.Value.ExpiresAt <= now))
        {
            _tickets.TryRemove(expired.Key, out _);
        }

        string ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _tickets[ticket] = (username, now + Lifetime);
        return ticket;
    }

    /// <summary>Returns the username the ticket was issued for and invalidates it, or null when unknown or expired.</summary>
    public string? Redeem(string ticket)
    {
        if (_tickets.TryRemove(ticket, out (string Username, DateTimeOffset ExpiresAt) entry) && entry.ExpiresAt > timeProvider.GetUtcNow())
        {
            return entry.Username;
        }

        return null;
    }
}
