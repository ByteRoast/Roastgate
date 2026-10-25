using System.Text.RegularExpressions;

namespace Roastgate.Core.Hosting;

public sealed record NginxProbeResult(bool Available, string? Version, string Output);

/// <summary>
/// Checks that nginx can be reached on the host, and reports its version and the modules it has loaded.
/// </summary>
public sealed partial class NginxProbe(IHostExec hostExec)
{
    [GeneratedRegex(@"nginx/(?<version>\d+(\.\d+)+)")]
    private static partial Regex VersionPattern();

    /// <summary>Reads the version from <c>nginx -v</c>.</summary>
    public async Task<NginxProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        HostCommandResult result = await hostExec.RunAsync(
            "nginx",
            ["-v"],
            TimeSpan.FromSeconds(10),
            cancellationToken
        );

        string output = result.CombinedOutput;

        if (!result.Succeeded)
        {
            return new NginxProbeResult(false, null, output);
        }

        Match match = VersionPattern().Match(output);

        return new NginxProbeResult(
            true,
            match.Success ? match.Groups["version"].Value : null,
            output
        );
    }

    /// <summary>
    /// Lists the dynamic modules nginx has loaded, from the <c>load_module</c> lines of <c>nginx -T</c>.
    /// Unknown when the command fails: a configuration that nginx refuses (for example because a module is missing) gives no answer.
    /// </summary>
    public async Task<NginxModules> ProbeModulesAsync(CancellationToken cancellationToken = default)
    {
        HostCommandResult result = await hostExec.RunAsync(
            "nginx",
            ["-T"],
            TimeSpan.FromSeconds(10),
            cancellationToken
        );

        return result.Succeeded
            ? NginxModules.Parse(result.CombinedOutput)
            : NginxModules.Unknown;
    }
}
