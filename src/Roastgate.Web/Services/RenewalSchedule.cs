namespace Roastgate.Web.Services;

/// <summary>
/// When the renewal job runs. Let's Encrypt recommends checking twice a day, at a time that is not on the hour.
/// </summary>
public sealed record RenewalSchedule(TimeSpan StartupDelay, TimeSpan Interval)
{
    public static RenewalSchedule Default { get; } = new(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromHours(12)
    );
}
