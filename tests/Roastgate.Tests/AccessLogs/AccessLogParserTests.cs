using System.Net;
using Roastgate.Core.AccessLogs;

namespace Roastgate.Tests.AccessLogs;

public sealed class AccessLogParserTests
{
    // nginx's default "combined" format.
    private const string CombinedLine =
        "20.106.103.160 - - [16/Nov/2026:12:31:48 +0100] \"GET / HTTP/1.1\" 200 612 \"-\" \"Mozilla/5.0 (X11; Linux x86_64)\"";

    [Fact]
    public void Reads_the_client_address_and_the_time_of_a_combined_line()
    {
        AccessLogEntry? entry = AccessLogParser.Parse(CombinedLine);

        Assert.NotNull(entry);
        Assert.Equal(IPAddress.Parse("20.106.103.160"), entry.Client);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 16, 12, 31, 48, TimeSpan.FromHours(1)),
            entry.Time
        );

        Assert.Equal(TimeSpan.FromHours(1), entry.Time.Offset);
    }

    [Fact]
    public void Reads_an_ipv6_client_address()
    {
        AccessLogEntry? entry = AccessLogParser.Parse(
            "2001:db8::1 - - [16/Nov/2026:12:31:48 +0100] \"GET / HTTP/2.0\" 200 612 \"-\" \"curl/8.5.0\""
        );

        Assert.NotNull(entry);
        Assert.Equal(IPAddress.Parse("2001:db8::1"), entry.Client);
    }

    [Fact]
    public void Keeps_a_negative_offset()
    {
        AccessLogEntry? entry = AccessLogParser.Parse(
            "20.106.103.160 - - [16/Nov/2026:07:31:48 -0500] \"GET / HTTP/1.1\" 200 612 \"-\" \"-\""
        );

        Assert.NotNull(entry);
        Assert.Equal(TimeSpan.FromHours(-5), entry.Time.Offset);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 16, 12, 31, 48, TimeSpan.Zero),
            entry.Time.ToUniversalTime()
        );
    }

    [Fact]
    public void Ignores_brackets_in_the_request_after_the_date()
    {
        AccessLogEntry? entry = AccessLogParser.Parse(
            "20.106.103.160 - - [16/Nov/2026:12:31:48 +0100] \"GET /search?q[]=a HTTP/1.1\" 200 612 \"-\" \"-\""
        );

        Assert.NotNull(entry);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 16, 12, 31, 48, TimeSpan.FromHours(1)),
            entry.Time
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 20.106.103.160 - - [16/Nov/2026:12:31:48 +0100] \"GET / HTTP/1.1\" 200 612")]
    [InlineData("20.106.103.160")]
    [InlineData("not-an-ip - - [16/Nov/2026:12:31:48 +0100] \"GET / HTTP/1.1\" 200 612")]
    [InlineData("20.106.103.160 - - \"GET / HTTP/1.1\" 200 612")]
    [InlineData("20.106.103.160 - - [16/Nov/2026:12:31:48 +0100")]
    [InlineData("20.106.103.160 - - [yesterday] \"GET / HTTP/1.1\" 200 612")]
    [InlineData("20.106.103.160 - - [2026-11-16T12:31:48+01:00] \"GET / HTTP/1.1\" 200 612")]
    public void Returns_null_for_a_line_that_is_not_a_combined_log_line(string line)
    {
        Assert.Null(AccessLogParser.Parse(line));
    }
}
