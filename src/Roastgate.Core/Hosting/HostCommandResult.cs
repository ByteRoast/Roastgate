namespace Roastgate.Core.Hosting;

/// <summary>Outcome of a command.</summary>
public sealed record HostCommandResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public const int NotFoundExitCode = 127;

    public bool Succeeded => ExitCode == 0 && !TimedOut;

    /// <summary>Both streams joined; nginx, for instance, prints its version and test results on stderr.</summary>
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput.Trim(), StandardError.Trim() }.Where((string part) => part.Length > 0)
    );
}
