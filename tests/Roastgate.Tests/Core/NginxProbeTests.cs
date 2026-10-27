using Roastgate.Core.Hosting;

namespace Roastgate.Tests.Core;

public sealed class NginxProbeTests
{
    private sealed class FakeHostExec(HostCommandResult result) : IHostExec
    {
        public Task<HostCommandResult> RunAsync(
            string program,
            IReadOnlyList<string> arguments,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Parses_the_version_nginx_prints_on_stderr()
    {
        NginxProbe probe = new(
            new FakeHostExec(
                new HostCommandResult(
                    0,
                    string.Empty,
                    "nginx version: nginx/1.27.3",
                    false
                )
            )
        );

        NginxProbeResult result = await probe.ProbeAsync();

        Assert.True(result.Available);
        Assert.Equal("1.27.3", result.Version);
    }

    [Fact]
    public async Task Reports_nginx_as_unavailable_when_the_command_fails()
    {
        NginxProbe probe = new(
            new FakeHostExec(
                new HostCommandResult(
                    127,
                    string.Empty,
                    "nsenter: failed to execute nginx",
                    false
                )
            )
        );

        NginxProbeResult result = await probe.ProbeAsync();

        Assert.False(result.Available);
        Assert.Null(result.Version);
        Assert.Contains("failed to execute", result.Output);
    }

    [Fact]
    public async Task Lists_the_modules_nginx_has_loaded()
    {
        string configuration = "# configuration file /etc/nginx/modules-enabled/50-mod-http-fancyindex.conf:\n" +
                               "load_module modules/ngx_http_fancyindex_module.so;\n";

        NginxProbe probe = new(
            new FakeHostExec(
                new HostCommandResult(
                    0,
                    configuration,
                    "nginx: configuration file test is successful",
                    false
                )
            )
        );

        NginxModules result = await probe.ProbeModulesAsync();

        Assert.True(result.Known);
        Assert.True(result.Has("ngx_http_fancyindex_module.so"));
    }

    [Fact]
    public async Task Reports_the_modules_as_unknown_when_the_command_fails()
    {
        NginxProbe probe = new(
            new FakeHostExec(
                new HostCommandResult(
                    1,
                    string.Empty,
                    "nginx: [emerg] unknown directive \"fancyindex\"", false
                )
            )
        );

        NginxModules result = await probe.ProbeModulesAsync();

        Assert.False(result.Known);
    }

    [Fact]
    public async Task Reports_the_modules_as_unknown_when_nginx_times_out()
    {
        NginxProbe probe = new(
            new FakeHostExec(
                new HostCommandResult(
                    0,
                    "load_module modules/x.so;",
                    string.Empty,
                    true
                )
            )
        );

        NginxModules result = await probe.ProbeModulesAsync();

        Assert.False(result.Known);
    }
}
