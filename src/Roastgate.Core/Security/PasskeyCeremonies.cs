using System.Collections.Concurrent;
using System;

namespace Roastgate.Core.Security;

/// <summary>
/// The server-side half of a passkey ceremony: the options (with the challenge) that were sent to the browser. Single use, five minutes,
/// so a recorded answer cannot be replayed and an old challenge cannot be answered later.
/// </summary>
public sealed class PasskeyCeremonies(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    // Anybody can start a sign-in ceremony: the table is bounded so that doing it in a loop cannot use up the memory.
    private const int MaximumPending = 1000;

    private readonly ConcurrentDictionary<string, PasskeyCeremony> _pending = new();

    public string Issue(PasskeyCeremonyKind kind, string optionsJson, string? ticket)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (KeyValuePair<string, PasskeyCeremony> expired in _pending.Where((
                     KeyValuePair<string, PasskeyCeremony> pair) => pair.Value.ExpiresAt <= now))
        {
            _pending.TryRemove(expired.Key, out _);
        }

        if (_pending.Count >= MaximumPending)
        {
            foreach (string oldest in _pending
                         .OrderBy((KeyValuePair<string, PasskeyCeremony> pair) => pair.Value.ExpiresAt)
                         .Take(MaximumPending / 10).Select((KeyValuePair<string, PasskeyCeremony> pair) => pair.Key))
            {
                _pending.TryRemove(oldest, out _);
            }
        }

        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        _pending[id] = new PasskeyCeremony(kind, optionsJson, ticket, now + Lifetime);

        return id;
    }

    /// <summary>The ceremony, which is used up by this call. Null when it is unknown, expired, used or of another kind.</summary>
    public PasskeyCeremony? Take(string id, PasskeyCeremonyKind kind)
    {
        if (_pending.TryRemove(id, out PasskeyCeremony? ceremony) && ceremony.Kind == kind &&
            ceremony.ExpiresAt > timeProvider.GetUtcNow())
        {
            return ceremony;
        }

        return null;
    }
}
