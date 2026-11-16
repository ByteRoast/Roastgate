using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Roastgate.Core;
using Roastgate.Core.AccessLogs;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.AccessLogs;

public sealed class AccessLogReaderTests : IDisposable
{
    // Fixed dates: the reader only compares them with `since`, never with the clock.
    private static readonly DateTimeOffset Today = new(
        2026,
        11,
        16,
        12,
        0,
        0,
        TimeSpan.Zero
    );

    private static readonly DateTimeOffset Yesterday = Today.AddDays(-1);
    private static readonly DateTimeOffset TwoDaysAgo = Today.AddDays(-2);
    private static readonly DateTimeOffset TenDaysAgo = Today.AddDays(-10);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "roastgate-access-logs-" + Guid.NewGuid().ToString("N")
    );

    private readonly RoastgateOptions _options;

    public AccessLogReaderTests()
    {
        Directory.CreateDirectory(_directory);

        _options = new RoastgateOptions { NginxLogDirectory = _directory };
    }

    public void Dispose()
    {
        TemporaryDirectory.Delete(_directory);
    }

    [Fact]
    public async Task Returns_nothing_when_the_log_directory_is_missing()
    {
        _options.NginxLogDirectory = Path.Combine(_directory, "missing");

        List<AccessLogEntry> entries = await ReadAsync(TenDaysAgo);

        Assert.Empty(entries);
    }

    [Fact]
    public async Task Reads_the_current_the_rotated_and_the_compressed_files()
    {
        WriteLog("access.log", Today, Line("10.0.0.1", Today.AddHours(-1)));
        WriteLog("access.log.1", Yesterday, Line("10.0.0.2", Yesterday.AddHours(-1)));
        WriteGzipLog(
            "access.log.2.gz",
            TwoDaysAgo,
            Line("10.0.0.3", TwoDaysAgo.AddHours(-1))
        );

        List<AccessLogEntry> entries = await ReadAsync(TenDaysAgo);

        Assert.Equal(
            ["10.0.0.1", "10.0.0.2", "10.0.0.3"],
            entries.Select(entry => entry.Client.ToString()).Order().ToList()
        );
    }

    [Fact]
    public async Task Skips_the_entries_older_than_since_inside_a_file()
    {
        WriteLog(
            "access.log",
            Today,
            Line("10.0.0.1", Today.AddHours(-5)),
            Line("10.0.0.2", Today.AddHours(-1))
        );

        List<AccessLogEntry> entries = await ReadAsync(Today.AddHours(-2));

        AccessLogEntry entry = Assert.Single(entries);
        Assert.Equal(IPAddress.Parse("10.0.0.2"), entry.Client);
    }

    [Fact]
    public async Task Does_not_open_the_files_last_written_before_since()
    {
        WriteLog("access.log", Today, Line("10.0.0.1", Today.AddHours(-1)));

        // Not gzip at all: opening it would throw. Its date alone must rule it out.
        await File.WriteAllTextAsync(Path.Combine(_directory, "access.log.9.gz"), "not gzip");

        File.SetLastWriteTimeUtc(Path.Combine(_directory, "access.log.9.gz"), TenDaysAgo.UtcDateTime);

        List<AccessLogEntry> entries = await ReadAsync(TwoDaysAgo);

        Assert.Single(entries);
    }

    [Fact]
    public async Task Keeps_reading_after_a_malformed_line()
    {
        WriteLog(
            "access.log",
            Today,
            Line("10.0.0.1", Today.AddHours(-3)),
            "this is not an access log line",
            "",
            Line("10.0.0.2", Today.AddHours(-1))
        );

        List<AccessLogEntry> entries = await ReadAsync(TenDaysAgo);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task Ignores_the_files_that_are_not_access_logs()
    {
        WriteLog("access.log", Today, Line("10.0.0.1", Today.AddHours(-1)));
        WriteLog("error.log", Today, Line("10.0.0.2", Today.AddHours(-1)));

        List<AccessLogEntry> entries = await ReadAsync(TenDaysAgo);

        AccessLogEntry entry = Assert.Single(entries);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), entry.Client);
    }

    private async Task<List<AccessLogEntry>> ReadAsync(DateTimeOffset since)
    {
        List<AccessLogEntry> entries = [];

        await foreach (AccessLogEntry entry in new AccessLogReader(_options).ReadAsync(since))
        {
            entries.Add(entry);
        }

        return entries;
    }

    // nginx writes the offset without a colon (+0000), not the "zzz" output of .NET (+00:00).
    private static string Line(string client, DateTimeOffset time)
    {
        string date = time.UtcDateTime.ToString("dd/MMM/yyyy:HH:mm:ss", CultureInfo.InvariantCulture);

        return $"{client} - - [{date} +0000] \"GET / HTTP/1.1\" 200 612 \"-\" \"-\"";
    }

    private void WriteLog(string name, DateTimeOffset lastWrite, params string[] lines)
    {
        string path = Path.Combine(_directory, name);

        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    private void WriteGzipLog(string name, DateTimeOffset lastWrite, params string[] lines)
    {
        string path = Path.Combine(_directory, name);

        using (FileStream file = File.Create(path))
        using (GZipStream gzip = new(file, CompressionMode.Compress))
        {
            gzip.Write(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"));
        }

        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }
}
