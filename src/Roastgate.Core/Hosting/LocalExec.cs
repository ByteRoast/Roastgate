using System.Diagnostics;

namespace Roastgate.Core.Hosting;

/// <summary>
/// Runs programs inside the Roastgate container itself (for tools shipped in the image, such as the embedded certbot).
/// </summary>
public interface ILocalExec
{
    Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class LocalExec : ILocalExec
{
    public Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            throw new ArgumentException("Program name is required.", nameof(program));
        }

        ProcessStartInfo startInfo = ProcessRunner.CreateStartInfo();
        startInfo.FileName = program;

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return ProcessRunner.RunAsync(startInfo, timeout, cancellationToken);
    }
}
