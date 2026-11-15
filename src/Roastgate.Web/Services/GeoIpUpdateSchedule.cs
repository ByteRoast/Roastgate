namespace Roastgate.Web.Services;

public sealed record GeoIpUpdateSchedule(TimeSpan StartupDelay, TimeSpan Interval)
{
    public static GeoIpUpdateSchedule Default { get; } = new(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromHours(24)
    );
}
