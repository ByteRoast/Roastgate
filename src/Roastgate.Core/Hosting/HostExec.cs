using System.Diagnostics;

namespace Roastgate.Core.Hosting;

public sealed class HostExec(RoastgateOptions options) : IHostExec
{
    public Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return ProcessRunner.RunAsync(
            BuildStartInfo(options.HostExecMode, program, arguments),
            timeout,
            cancellationToken
        );
    }

    internal static ProcessStartInfo BuildStartInfo(HostExecMode mode, string program, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            throw new ArgumentException("Program name is required.", nameof(program));
        }

        ProcessStartInfo startInfo = ProcessRunner.CreateStartInfo();

        if (mode == HostExecMode.Nsenter)
        {
            startInfo.FileName = "nsenter";

            foreach (
                string argument in new[]
                {
                    "--target", "1", "--mount", "--uts", "--ipc", "--net", "--pid", "--", program
                }
            )
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        else
        {
            startInfo.FileName = program;
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
