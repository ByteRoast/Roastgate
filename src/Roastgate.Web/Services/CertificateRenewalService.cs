using Roastgate.Core.Certificates;

namespace Roastgate.Web.Services;

/// <summary>
/// Runs <see cref="ICertificateRenewalJob"/> in the background. The job itself decides whether anything is to be done.
/// </summary>
public sealed class CertificateRenewalService(
    ICertificateRenewalJob job,
    RenewalSchedule schedule,
    ILogger<CertificateRenewalService> logger
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
                    logger.LogError(exception, "The certificate renewal pass failed.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // The application is shutting down.
        }
    }
}
