namespace Roastgate.Core.Security;

/// <summary>
/// In-memory brute force protection: after <see cref="MaxFailures"/> failures within <see cref="Window"/>,
/// the key (an IP address, or a fixed name for the setup token) is blocked until the oldest failure leaves the window.
/// </summary>
public sealed class LoginThrottle(TimeProvider timeProvider)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private const int PruneThreshold = 1000;

    private readonly Dictionary<string, Queue<DateTimeOffset>> _failures = [];
    private readonly object _lock = new();

    public bool IsBlocked(string key, out TimeSpan retryAfter)
    {
        lock (_lock)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (_failures.TryGetValue(key, out Queue<DateTimeOffset>? failures))
            {
                Trim(failures, now);
                if (failures.Count >= MaxFailures)
                {
                    retryAfter = failures.Peek() + Window - now;
                    return true;
                }
            }

            retryAfter = TimeSpan.Zero;
            return false;
        }
    }

    public void RecordFailure(string key)
    {
        lock (_lock)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (_failures.Count > PruneThreshold)
            {
                PruneAll(now);
            }

            if (!_failures.TryGetValue(key, out Queue<DateTimeOffset>? failures))
            {
                failures = new Queue<DateTimeOffset>();
                _failures[key] = failures;
            }

            Trim(failures, now);
            failures.Enqueue(now);
        }
    }

    public void Reset(string key)
    {
        lock (_lock)
        {
            _failures.Remove(key);
        }
    }

    private static void Trim(Queue<DateTimeOffset> failures, DateTimeOffset now)
    {
        while (failures.Count > 0 && now - failures.Peek() >= Window)
        {
            failures.Dequeue();
        }
    }

    private void PruneAll(DateTimeOffset now)
    {
        foreach (string key in _failures.Keys.ToList())
        {
            Trim(_failures[key], now);
            if (_failures[key].Count == 0)
            {
                _failures.Remove(key);
            }
        }
    }
}
