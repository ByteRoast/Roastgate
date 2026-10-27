namespace Roastgate.Core.Hosting;

/// <summary>
/// The single entry point for running programs on the host. Arguments are never passed through a shell.
/// </summary>
public interface IHostExec
{
    Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    );
}
