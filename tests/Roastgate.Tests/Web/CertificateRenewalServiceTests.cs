using Microsoft.Extensions.Logging.Abstractions;
using Roastgate.Core.Certificates;
using Roastgate.Web.Services;

namespace Roastgate.Tests.Web;

public sealed class CertificateRenewalServiceTests
{
    private sealed class CountingJob(Func<int, Task> onRun) : ICertificateRenewalJob
    {
        private int _runs;

        public int Runs => _runs;

        public Task RunOnceAsync(CancellationToken cancellationToken = default)
        {
            int number = Interlocked.Increment(ref _runs);
            return onRun(number);
        }

        public Task<RenewalStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RenewalStatus(null, null, null));
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task The_job_runs_after_the_startup_delay_and_then_on_every_tick()
    {
        CountingJob job = new((int _) => Task.CompletedTask);
        CertificateRenewalService service = new(job, new RenewalSchedule(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)), NullLogger<CertificateRenewalService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => job.Runs >= 3);
        await service.StopAsync(CancellationToken.None);

        Assert.True(job.Runs >= 3, $"expected at least 3 runs, saw {job.Runs}");
    }

    [Fact]
    public async Task Nothing_runs_before_the_startup_delay_has_passed()
    {
        CountingJob job = new((int _) => Task.CompletedTask);
        CertificateRenewalService service = new(job, new RenewalSchedule(TimeSpan.FromHours(1), TimeSpan.FromHours(1)), NullLogger<CertificateRenewalService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, job.Runs);
    }

    [Fact]
    public async Task A_pass_that_throws_does_not_stop_the_next_ones()
    {
        CountingJob job = new((int number) => number == 1 ? throw new InvalidOperationException("certbot exploded") : Task.CompletedTask);
        CertificateRenewalService service = new(job, new RenewalSchedule(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)), NullLogger<CertificateRenewalService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => job.Runs >= 3);
        await service.StopAsync(CancellationToken.None);

        Assert.True(job.Runs >= 3);
    }

    [Fact]
    public async Task Stopping_while_a_pass_is_running_ends_cleanly()
    {
        CountingJob job = new(async (int _) => await Task.Delay(Timeout.Infinite, CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(50)).ContinueWith((Task _) => { }));
        CertificateRenewalService service = new(job, new RenewalSchedule(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)), NullLogger<CertificateRenewalService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => job.Runs >= 1);
        Task stopping = service.StopAsync(CancellationToken.None);

        Assert.True(await Task.WhenAny(stopping, Task.Delay(5000)) == stopping, "the service did not stop");
    }

    [Fact]
    public void The_default_schedule_checks_twice_a_day_after_a_short_delay()
    {
        Assert.Equal(TimeSpan.FromHours(12), RenewalSchedule.Default.Interval);
        Assert.Equal(TimeSpan.FromMinutes(5), RenewalSchedule.Default.StartupDelay);
    }
}
