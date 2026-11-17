using System.Net;
using MaxMind.Db;
using Roastgate.Core;
using Roastgate.Core.GeoIP;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.GeoIP;

public sealed class GeoLocatorTests : IDisposable
{
    // MaxMind's public test database (see Fixtures/README.md): 81.2.69.142 is in London.
    private static readonly string Fixture = Path.Combine(
        AppContext.BaseDirectory,
        "GeoIP",
        "Fixtures",
        "GeoIP2-City-Test.mmdb"
    );

    private static readonly IPAddress LondonAddress = IPAddress.Parse("81.2.69.142");

    // Only created by the tests that need files; the others see a missing database.
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "roastgate-geoip-" + Guid.NewGuid().ToString("N")
    );

    private readonly RoastgateOptions _options;

    public GeoLocatorTests()
    {
        _options = new RoastgateOptions { DataDirectory = _directory };
    }

    public void Dispose()
    {
        TemporaryDirectory.Delete(_directory);
    }

    [Fact]
    public void Is_unavailable_when_the_database_file_is_missing()
    {
        using GeoLocator locator = new(_options);

        Assert.False(locator.IsAvailable);
        Assert.Null(locator.BuildDate);
    }

    [Fact]
    public void Locate_returns_null_when_the_database_file_is_missing()
    {
        using GeoLocator locator = new(_options);

        Assert.Null(locator.Locate(LondonAddress));
    }

    [Fact]
    public void Locates_an_address_from_an_existing_database()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        using GeoLocator locator = new(_options);

        Assert.True(locator.IsAvailable);
        Assert.NotNull(locator.BuildDate);
        Assert.Equal("London", locator.Locate(LondonAddress)?.City);
    }

    [Fact]
    public void Locate_returns_the_iso_code_of_the_country()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        using GeoLocator locator = new(_options);

        IpLocation? location = locator.Locate(LondonAddress);

        Assert.NotNull(location);
        Assert.Equal("United Kingdom", location.Country);
        Assert.Equal("GB", location.CountryCode);
    }

    [Fact]
    public void Locate_returns_null_for_an_address_missing_from_the_database()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        using GeoLocator locator = new(_options);

        Assert.Null(locator.Locate(IPAddress.Parse("172.17.0.1")));
    }

    [Fact]
    public void Replace_loads_a_database_when_none_was_there()
    {
        string downloaded = CopyFixtureAsDownload();

        using GeoLocator locator = new(_options);

        locator.Replace(downloaded);

        Assert.True(locator.IsAvailable);
        Assert.Equal("London", locator.Locate(LondonAddress)?.City);
        Assert.True(File.Exists(_options.GeoIpDatabasePath));
        Assert.False(File.Exists(downloaded));
    }

    [Fact]
    public void Replace_swaps_the_database_that_is_in_use()
    {
        string downloaded = CopyFixtureAsDownload();

        File.Copy(Fixture, _options.GeoIpDatabasePath);

        using GeoLocator locator = new(_options);

        Assert.True(locator.IsAvailable);

        locator.Replace(downloaded);

        Assert.True(locator.IsAvailable);
        Assert.Equal("London", locator.Locate(LondonAddress)?.City);
        Assert.False(File.Exists(downloaded));
    }

    [Fact]
    public void Replace_with_a_corrupt_file_leaves_the_locator_unavailable_instead_of_broken()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        string corrupt = Path.Combine(_directory, "corrupt.mmdb.tmp");

        File.WriteAllText(corrupt, "not a MaxMind database");

        using GeoLocator locator = new(_options);

        Assert.Throws<InvalidDatabaseException>(() => locator.Replace(corrupt));

        // The old reader was closed: lookups must return null, not throw ObjectDisposedException.
        Assert.False(locator.IsAvailable);
        Assert.Null(locator.Locate(LondonAddress));
    }

    private string CopyFixtureAsDownload()
    {
        Directory.CreateDirectory(_directory);

        string downloaded = _options.GeoIpDatabasePath + ".tmp";

        File.Copy(Fixture, downloaded);

        return downloaded;
    }
}
