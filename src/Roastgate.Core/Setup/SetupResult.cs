namespace Roastgate.Core.Setup;

public sealed record SetupResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static SetupResult Success { get; } = new(true, []);

    public static SetupResult Failure(params string[] errors)
    {
        return new SetupResult(false, errors);
    }
}
