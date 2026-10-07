using Roastgate.Core.Data;

namespace Roastgate.Core.Security;

/// <summary>
/// The security stamp of the account: a random value stored in every session cookie and checked on each request. When the password
/// (or the two-factor setup) really changes, the stamp is renewed, and every session opened before that moment stops working. That is what makes
/// "change the password" actually lock out somebody who stole a session.
/// </summary>
public interface ISessionStamp
{
    /// <summary>The current fingerprint, or null when there is no administrator yet.</summary>
    Task<string?> CurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Forget the cached value after the credentials changed in this process.</summary>
    void Invalidate();
}

public sealed class SessionStamp(IAdminAccountStore store, TimeProvider timeProvider) : ISessionStamp
{
    // The CLI changes the password from another process, so the value cannot be cached for long.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private string? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private bool _hasValue;

    public async Task<string?> CurrentAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_hasValue && timeProvider.GetUtcNow() - _cachedAt < CacheLifetime)
            {
                return _cached;
            }
        }

        AdminAccount? account = await store.GetAsync(cancellationToken);
        string? stamp = account?.SecurityStamp;
        lock (_lock)
        {
            _cached = stamp;
            _cachedAt = timeProvider.GetUtcNow();
            _hasValue = true;
        }

        return stamp;
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _hasValue = false;
        }
    }
}
