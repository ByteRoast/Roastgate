using Roastgate.Core.Hosting;

namespace Roastgate.Tests.Support;

/// <summary>An <see cref="IHostExec"/> that answers from a script instead of running anything.</summary>
internal sealed class ScriptedHostExec(Func<string, IReadOnlyList<string>, HostCommandResult> script) : IHostExec
{
    public static HostCommandResult Ok(string standardOutput = "", string standardError = "")
    {
        return new HostCommandResult(0, standardOutput, standardError, false);
    }

    public static HostCommandResult NotFound()
    {
        return new HostCommandResult(HostCommandResult.NotFoundExitCode, string.Empty, "not found", false);
    }

    public Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(script(program, arguments));
    }
}
