using System.Globalization;
using Roastgate.Core;
using Roastgate.Core.AccessLogs;
using Roastgate.Core.GeoIP;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.AccessLogs;

public sealed class TrafficMapTests : IDisposable
{
    // MaxMind's public test database (see GeoIP/Fixtures/README.md). In it, 81.2.69.142 and 81.2.69.144 are two London
    // addresses with the same coordinates, and 216.160.83.56 is in Milton (US).
    private static readonly string Fixture = Path.Combine(
        AppContext.BaseDirectory,
        "GeoIP",
        "Fixtures",
        "GeoIP2-City-Test.mmdb"
    );

    private const string LondonA = "81.2.69.142";
    private const string LondonB = "81.2.69.144";
    private const string Milton = "216.160.83.56";
    private const string Private = "192.168.1.10";

    private static readonly DateTimeOffset Now = new(
        2026,
        11,
        16,
        12,
        0,
        0,
        TimeSpan.Zero
    );

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "roastgate-traffic-map-" + Guid.NewGuid().ToString("N")
    );

    private readonly RoastgateOptions _options;

    public TrafficMapTests()
    {
        _options = new RoastgateOptions
        {
            DataDirectory = Path.Combine(_directory, "data"),
            NginxLogDirectory = Path.Combine(_directory, "logs"),
        };

        Directory.CreateDirectory(_options.DataDirectory);
        Directory.CreateDirectory(_options.NginxLogDirectory);
    }

    public void Dispose()
    {
        TemporaryDirectory.Delete(_directory);
    }

    [Fact]
    public async Task Groups_the_addresses_of_the_same_place_into_one_point()
    {
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        WriteAccessLog(
            Requests(LondonA, 3)
                .Concat(Requests(LondonB, 2))
                .Concat(Requests(Milton, 1))
        );

        TrafficSnapshot snapshot = await BuildAsync();

        Assert.Equal(2, snapshot.Points.Count);

        TrafficPoint london = Assert.Single(snapshot.Points, point => point.City == "London");

        Assert.Equal(5, london.Requests);
        Assert.Equal(2, london.Addresses);
        Assert.Equal(51.5142, london.Latitude);
        Assert.Equal(-0.0931, london.Longitude);
        Assert.Equal("GB", london.CountryCode);

        TrafficPoint milton = Assert.Single(snapshot.Points, point => point.City == "Milton");

        Assert.Equal(1, milton.Requests);
        Assert.Equal(1, milton.Addresses);
        Assert.Equal("US", milton.CountryCode);
    }

    [Fact]
    public async Task Counts_the_requests_of_addresses_that_cannot_be_located()
    {
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        WriteAccessLog(Requests(LondonA, 1).Concat(Requests(Private, 4)));

        TrafficSnapshot snapshot = await BuildAsync();

        Assert.Single(snapshot.Points);
        Assert.Equal(4, snapshot.UnlocatedRequests);
    }

    [Fact]
    public async Task Counts_every_request_as_unlocated_when_the_geoip_database_is_missing()
    {
        WriteAccessLog(Requests(LondonA, 3).Concat(Requests(Milton, 2)));

        TrafficSnapshot snapshot = await BuildAsync();

        Assert.Empty(snapshot.Points);
        Assert.Equal(5, snapshot.UnlocatedRequests);
    }

    [Fact]
    public async Task Returns_an_empty_snapshot_when_there_is_no_traffic()
    {
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        TrafficSnapshot snapshot = await BuildAsync();

        Assert.Empty(snapshot.Points);
        Assert.Equal(0, snapshot.UnlocatedRequests);
    }

    private async Task<TrafficSnapshot> BuildAsync()
    {
        using GeoLocator geoLocator = new(_options);

        TrafficMap map = new(new AccessLogReader(_options), geoLocator);

        return await map.BuildAsync(Now.AddDays(-1));
    }

    private static IEnumerable<string> Requests(string client, int count)
    {
        string date = Now.AddHours(-1).UtcDateTime.ToString("dd/MMM/yyyy:HH:mm:ss", CultureInfo.InvariantCulture);

        return Enumerable.Repeat($"{client} - - [{date} +0000] \"GET / HTTP/1.1\" 200 612 \"-\" \"-\"", count);
    }

    private void WriteAccessLog(IEnumerable<string> lines)
    {
        string path = Path.Combine(_options.NginxLogDirectory, "access.log");

        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, Now.UtcDateTime);
    }
}
