using System.Text.RegularExpressions;

namespace Roastgate.Core.Hosting;

public sealed record NginxProbeResult(bool Available, string? Version, string Output);

/// <summary>Checks that nginx can be reached on the host and reports its version.</summary>
public sealed partial class NginxProbe(IHostExec hostExec)
{
    [GeneratedRegex(@"nginx/(?<version>\d+(\.\d+)+)")]
    private static partial Regex VersionPattern();

    public async Task<NginxProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        HostCommandResult result = await hostExec.RunAsync("nginx", ["-v"], TimeSpan.FromSeconds(10), cancellationToken);
        string output = result.CombinedOutput;

        if (!result.Succeeded)
        {
            return new NginxProbeResult(false, null, output);
        }

        Match match = VersionPattern().Match(output);
        return new NginxProbeResult(true, match.Success ? match.Groups["version"].Value : null, output);
    }
}
