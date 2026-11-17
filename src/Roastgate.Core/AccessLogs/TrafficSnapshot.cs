namespace Roastgate.Core.AccessLogs;

public sealed record TrafficSnapshot(IReadOnlyList<TrafficPoint> Points, int UnlocatedRequests);
