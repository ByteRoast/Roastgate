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
        NginxProbe probe = new(new FakeHostExec(new HostCommandResult(0, string.Empty, "nginx version: nginx/1.27.3", false)));

        NginxProbeResult result = await probe.ProbeAsync();

        Assert.True(result.Available);
        Assert.Equal("1.27.3", result.Version);
    }

    [Fact]
    public async Task Reports_nginx_as_unavailable_when_the_command_fails()
    {
        NginxProbe probe = new(new FakeHostExec(new HostCommandResult(127, string.Empty, "nsenter: failed to execute nginx", false)));

        NginxProbeResult result = await probe.ProbeAsync();

        Assert.False(result.Available);
        Assert.Null(result.Version);
        Assert.Contains("failed to execute", result.Output);
    }
}
