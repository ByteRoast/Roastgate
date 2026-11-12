using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;

namespace Roastgate.Core.Security;

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
