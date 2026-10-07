using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core.Certificates;
using Roastgate.Core.Hosting;
using Roastgate.Core.Settings;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Certificates;

public sealed class CertificateRenewalJobTests
{
    private static async Task<(NginxTestbed Nginx, ScriptedLocalExec Local, RecordingHostExec Host)> CreateAsync(CertbotMode mode)
    {
        ScriptedLocalExec local = new((string _, IReadOnlyList<string> _) => new HostCommandResult(0, "No renewals were attempted.", string.Empty, false));
        RecordingHostExec host = new((string _, IReadOnlyList<string> _) => new HostCommandResult(0, string.Empty, string.Empty, false));
        NginxTestbed nginx = await NginxTestbed.CreateAsync(
            configureServices: (IServiceCollection services) =>
            {
                services.AddSingleton<ILocalExec>(local);
                services.AddSingleton<IHostExec>(host);
            },
            configureSettings: (AppSettings settings) => settings with { CertbotMode = mode });
        return (nginx, local, host);
    }

    [Fact]
    public async Task With_the_embedded_certbot_the_job_renews_and_remembers_when()
    {
        (NginxTestbed nginx, ScriptedLocalExec local, _) = await CreateAsync(CertbotMode.Embedded);
        await using NginxTestbed owner = nginx;
        ICertificateRenewalJob job = nginx.Database.Get<ICertificateRenewalJob>();
        Assert.Null((await job.GetStatusAsync()).LastRun);

        await job.RunOnceAsync();

        Assert.Equal("renew", Assert.Single(local.Calls).Arguments[0]);
        RenewalStatus status = await job.GetStatusAsync();
        Assert.NotNull(status.LastRun);
        Assert.True(status.LastSucceeded);
        Assert.Contains("Nothing to renew", status.Summary);
    }

    [Theory]
    [InlineData(CertbotMode.Host)]
    [InlineData(CertbotMode.Disabled)]
    public async Task The_job_leaves_renewal_to_the_host_certbot_or_does_nothing_when_disabled(CertbotMode mode)
    {
        (NginxTestbed nginx, ScriptedLocalExec local, RecordingHostExec host) = await CreateAsync(mode);
        await using NginxTestbed owner = nginx;
        ICertificateRenewalJob job = nginx.Database.Get<ICertificateRenewalJob>();

        await job.RunOnceAsync();

        Assert.Empty(local.Calls);
        Assert.Empty(host.Calls);
        Assert.Null((await job.GetStatusAsync()).LastRun);
    }

    [Fact]
    public async Task A_failing_run_is_recorded_as_a_failure()
    {
        ScriptedLocalExec local = new((string _, IReadOnlyList<string> _) => new HostCommandResult(1, string.Empty, "Another instance of Certbot is already running.", false));
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync(
            configureServices: (IServiceCollection services) => services.AddSingleton<ILocalExec>(local));
        TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], DateTimeOffset.UtcNow.AddDays(3));
        ICertificateRenewalJob job = nginx.Database.Get<ICertificateRenewalJob>();

        await job.RunOnceAsync();

        RenewalStatus status = await job.GetStatusAsync();
        Assert.False(status.LastSucceeded);
        Assert.Contains("could not renew", status.Summary);
    }
}
