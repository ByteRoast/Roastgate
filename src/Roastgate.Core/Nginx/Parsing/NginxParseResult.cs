namespace Roastgate.Core.Nginx.Parsing;

/// <param name="Error">Why the text could not be read (with a line number), or null.</param>
public sealed record NginxParseResult(IReadOnlyList<NginxStatement> Statements, string? Error)
{
    public bool Succeeded => Error is null;
}
