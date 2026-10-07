using Roastgate.Core.Hosting;

namespace Roastgate.Core.Nginx;

public sealed record NginxResult(bool Succeeded, string Output);

public interface INginxController
{
    /// <summary>Runs <c>nginx -t</c> on the host.</summary>
    Task<NginxResult> TestAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs <c>nginx -s reload</c> on the host.</summary>
    Task<NginxResult> ReloadAsync(CancellationToken cancellationToken = default);
}

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
