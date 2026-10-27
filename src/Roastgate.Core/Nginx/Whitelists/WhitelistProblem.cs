namespace Roastgate.Core.Nginx.Whitelists;

public sealed record WhitelistProblem(ProblemSeverity Severity, int? EntryIndex, string Message);
