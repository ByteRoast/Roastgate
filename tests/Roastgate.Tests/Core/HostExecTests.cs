using System.Diagnostics;
using Roastgate.Core;
using Roastgate.Core.Hosting;

namespace Roastgate.Tests.Core;

public sealed class HostExecTests
{
    private static HostExec CreateDirect()
    {
        return new HostExec(new RoastgateOptions { HostExecMode = HostExecMode.Direct });
    }

    [Fact]
    public async Task Direct_mode_runs_the_program_and_captures_output()
    {
        HostCommandResult result = await CreateDirect().RunAsync("dotnet", ["--version"]);

        Assert.True(result.Succeeded);
        Assert.Matches(@"^\d+\.\d+\.\d+", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task Missing_program_is_reported_as_not_found_instead_of_throwing()
    {
        HostCommandResult result = await CreateDirect().RunAsync("roastgate-no-such-program", []);

        Assert.False(result.Succeeded);
        Assert.Equal(HostCommandResult.NotFoundExitCode, result.ExitCode);
    }

    [Fact]
    public void Nsenter_mode_wraps_the_command_in_the_host_namespaces()
    {
        ProcessStartInfo startInfo = HostExec.BuildStartInfo(HostExecMode.Nsenter, "nginx", ["-t", "-q"]);

        Assert.Equal("nsenter", startInfo.FileName);
        Assert.Equal(
            ["--target", "1", "--mount", "--uts", "--ipc", "--net", "--pid", "--", "nginx", "-t", "-q"],
            startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void Arguments_are_never_merged_into_a_shell_string()
    {
        ProcessStartInfo startInfo = HostExec.BuildStartInfo(HostExecMode.Direct, "nginx", ["-g", "daemon off; evil"]);

        Assert.Equal("nginx", startInfo.FileName);
        Assert.Equal(["-g", "daemon off; evil"], startInfo.ArgumentList.ToArray());
        Assert.Empty(startInfo.Arguments);
    }

    [Fact]
    public void Blank_program_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => HostExec.BuildStartInfo(HostExecMode.Direct, "  ", []));
    }
}
