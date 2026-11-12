namespace Roastgate.Core.Hosting;

public sealed record NginxProbeResult(bool Available, string? Version, string Output);
