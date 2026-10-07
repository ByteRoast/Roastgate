using System.ComponentModel;
using System.Diagnostics;

namespace Roastgate.Core.Hosting;

/// <summary>Outcome of a command.</summary>
public sealed record HostCommandResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public const int NotFoundExitCode = 127;

    public bool Succeeded => ExitCode == 0 && !TimedOut;

    /// <summary>Both streams joined; nginx, for instance, prints its version and test results on stderr.</summary>
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput.Trim(), StandardError.Trim() }.Where((string part) => part.Length > 0));
}

/// <summary>The single entry point for running programs on the host. Arguments are never passed through a shell.</summary>
public interface IHostExec
{
    Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs programs inside the Roastgate container itself (for tools shipped in the image, such as the embedded certbot).</summary>
public interface ILocalExec
{
    Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

public sealed class HostExec(RoastgateOptions options) : IHostExec
{
    public Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return ProcessRunner.RunAsync(BuildStartInfo(options.HostExecMode, program, arguments), timeout, cancellationToken);
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
            foreach (string argument in new[] { "--target", "1", "--mount", "--uts", "--ipc", "--net", "--pid", "--", program })
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

public sealed class LocalExec : ILocalExec
{
    public Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
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

internal static class ProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static ProcessStartInfo CreateStartInfo()
    {
        return new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    public static async Task<HostCommandResult> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using Process process = new() { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            return new HostCommandResult(HostCommandResult.NotFoundExitCode, string.Empty, exception.Message, false);
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return new HostCommandResult(-1, await standardOutput, await standardError, true);
        }

        return new HostCommandResult(process.ExitCode, await standardOutput, await standardError, false);
    }
}
