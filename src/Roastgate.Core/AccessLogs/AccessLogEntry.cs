using System.Net;

namespace Roastgate.Core.AccessLogs;

public sealed record AccessLogEntry(IPAddress Client, DateTimeOffset Time);
