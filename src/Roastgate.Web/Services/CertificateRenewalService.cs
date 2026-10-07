using Roastgate.Core.Certificates;

namespace Roastgate.Web.Services;

/// <summary>When the renewal job runs. Let's Encrypt recommends checking twice a day, at a time that is not on the hour.</summary>
public sealed record RenewalSchedule(TimeSpan StartupDelay, TimeSpan Interval)
{
    public static RenewalSchedule Default { get; } = new(TimeSpan.FromMinutes(5), TimeSpan.FromHours(12));
}

/// <summary>Runs <see cref="ICertificateRenewalJob"/> in the background. The job itself decides whether anything is to be done.</summary>
public sealed class CertificateRenewalService(
    ICertificateRenewalJob job,
    RenewalSchedule schedule,
    ILogger<CertificateRenewalService> logger) : BackgroundService
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
                    logger.LogError(exception, "The certificate renewal pass failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // The application is shutting down.
        }
    }
}
