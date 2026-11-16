using System.Globalization;
using System.Net;

namespace Roastgate.Core.AccessLogs;

public static class AccessLogParser
{
    public static AccessLogEntry? Parse(string line)
    {
        int firstSpace = line.IndexOf(' ');

        if (firstSpace <= 0)
        {
            // empty line or starting with a space -> not a log line
            return null;
        }

        if (!IPAddress.TryParse(line.AsSpan(0, firstSpace), out IPAddress? ipAddress))
        {
            return null;
        }

        int open = line.IndexOf('[', firstSpace);
        int close = open < 0 ? -1 : line.IndexOf(']', open);

        if (close < 0)
        {
            // no date between []
            return null;
        }

        ReadOnlySpan<char> text = line.AsSpan(open + 1, close - open - 1);

        return DateTimeOffset.TryParseExact(
            text,
            "dd/MMM/yyyy:HH:mm:ss zzz",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset time
        )
            ? new AccessLogEntry(ipAddress, time)
            : null;
    }
}
