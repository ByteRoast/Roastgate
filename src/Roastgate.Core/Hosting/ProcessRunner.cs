using System.ComponentModel;
using System.Diagnostics;

namespace Roastgate.Core.Hosting;

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

    public static async Task<HostCommandResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan? timeout,
        CancellationToken cancellationToken
    )
    {
        using Process process = new() { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            return new HostCommandResult(
                HostCommandResult.NotFoundExitCode,
                string.Empty,
                exception.Message,
                false
            );
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );

        timeoutSource.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);

            return new HostCommandResult(
                -1,
                await standardOutput,
                await standardError,
                true
            );
        }

        return new HostCommandResult(
            process.ExitCode,
            await standardOutput,
            await standardError,
            false
        );
    }
}
