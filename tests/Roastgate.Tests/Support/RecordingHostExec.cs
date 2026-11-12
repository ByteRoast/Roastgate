using Roastgate.Core.Hosting;

namespace Roastgate.Tests.Support;

/// <summary>An <see cref="IHostExec"/> that answers from a script and remembers every call.</summary>
internal sealed class RecordingHostExec(Func<string, IReadOnlyList<string>, HostCommandResult> script) : IHostExec
{
    public List<(string Program, string[] Arguments)> Calls { get; } = [];

    public Task<HostCommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Calls.Add((program, arguments.ToArray()));

        return Task.FromResult(script(program, arguments));
    }
}
