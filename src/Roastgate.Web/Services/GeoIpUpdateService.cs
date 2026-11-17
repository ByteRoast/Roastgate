using Roastgate.Core.GeoIP;

namespace Roastgate.Web.Services;

public sealed class GeoIpUpdateService(
    GeoIpUpdater job,
    GeoIpUpdateSchedule schedule,
    ILogger<GeoIpUpdateService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(schedule.StartupDelay, stoppingToken);

            using PeriodicTimer timer = new(schedule.Interval);

            do
            {
                try
                {
                    await job.RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A failed pass must never stop the timer: the next one may well succeed.
                    logger.LogError(exception, "The GeoIP update pass failed.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // The application is shutting down.
        }
    }
}
