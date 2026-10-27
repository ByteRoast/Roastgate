using Roastgate.Core.Hosting;

namespace Roastgate.Core.Nginx;

public sealed class NginxController(IHostExec hostExec) : INginxController
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public Task<NginxResult> TestAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(["-t"], cancellationToken);
    }

    public Task<NginxResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(["-s", "reload"], cancellationToken);
    }

    private async Task<NginxResult> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        HostCommandResult result = await hostExec.RunAsync("nginx", arguments, Timeout, cancellationToken);

        return new NginxResult(result.Succeeded, result.CombinedOutput);
    }
}
