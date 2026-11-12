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
