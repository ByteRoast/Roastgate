namespace Roastgate.Core.Certificates;

public sealed record RenewalStatus(DateTimeOffset? LastRun, bool? LastSucceeded, string? Summary);
