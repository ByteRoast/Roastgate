using System.IO.Compression;
using AxiosNet.Client;
using AxiosNet.Config;
using MaxMind.Db;
using Microsoft.Extensions.Time.Testing;
using Roastgate.Core;
using Roastgate.Core.GeoIP;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.GeoIP;

public sealed class GeoIpUpdaterTests : IDisposable
{
    // MaxMind's public test database (see Fixtures/README.md), built years before "now" below: it always counts as
    // outdated.
    private static readonly string Fixture = Path.Combine(
        AppContext.BaseDirectory,
        "GeoIP",
        "Fixtures",
        "GeoIP2-City-Test.mmdb"
    );

    private const string CurrentMonthUrl = "https://download.db-ip.com/free/dbip-city-lite-2030-03.mmdb.gz";
    private const string PreviousMonthUrl = "https://download.db-ip.com/free/dbip-city-lite-2030-02.mmdb.gz";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "roastgate-geoip-update-" + Guid.NewGuid().ToString("N")
    );

    private readonly RoastgateOptions _options;

    // Not readonly: FakeTimeProvider cannot go back in time, so a test that needs an earlier date replaces it.
    private FakeTimeProvider _time = new(
        new DateTimeOffset(2030, 3, 15, 12, 0, 0, TimeSpan.Zero)
    );

    public GeoIpUpdaterTests()
    {
        Directory.CreateDirectory(_directory);

        _options = new RoastgateOptions { DataDirectory = _directory, AutoUpdateEnabled = true };
    }

    public void Dispose()
    {
        TemporaryDirectory.Delete(_directory);
    }

    [Fact]
    public async Task Does_nothing_when_auto_update_is_disabled()
    {
        _options.AutoUpdateEnabled = false;

        ScriptedHttpHandler http = new(_ => ScriptedHttpHandler.Ok(GzippedFixture()));

        using GeoLocator locator = new(_options);

        await Updater(http, locator).RunOnceAsync();

        Assert.Empty(http.Requests);
        Assert.False(locator.IsAvailable);
    }

    [Fact]
    public async Task Does_nothing_when_the_loaded_database_is_from_the_current_month()
    {
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        using GeoLocator locator = new(_options);

        DateTime buildDate = locator.BuildDate!.Value;

        _time = new FakeTimeProvider(
            new DateTimeOffset(buildDate.Year, buildDate.Month, 28, 12, 0, 0, TimeSpan.Zero)
        );

        ScriptedHttpHandler http = new(_ => ScriptedHttpHandler.Ok(GzippedFixture()));

        await Updater(http, locator).RunOnceAsync();

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Downloads_the_current_month_when_there_is_no_database()
    {
        ScriptedHttpHandler http = new(_ => ScriptedHttpHandler.Ok(GzippedFixture()));

        using GeoLocator locator = new(_options);

        await Updater(http, locator).RunOnceAsync();

        Assert.Equal([CurrentMonthUrl], http.Requests);
        Assert.True(locator.IsAvailable);
        Assert.True(File.Exists(_options.GeoIpDatabasePath));
        Assert.False(File.Exists(TmpPath));
    }

    [Fact]
    public async Task Replaces_an_outdated_database_with_the_current_month()
    {
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        ScriptedHttpHandler http = new(_ => ScriptedHttpHandler.Ok(GzippedFixture()));

        using GeoLocator locator = new(_options);

        await Updater(http, locator).RunOnceAsync();

        Assert.Equal([CurrentMonthUrl], http.Requests);
        Assert.True(locator.IsAvailable);
        Assert.False(File.Exists(TmpPath));
    }

    [Fact]
    public async Task Falls_back_to_the_previous_month_when_there_is_no_database_and_the_current_one_is_not_published()
    {
        ScriptedHttpHandler http = new(url => url == CurrentMonthUrl
            ? ScriptedHttpHandler.NotFound()
            : ScriptedHttpHandler.Ok(GzippedFixture()));

        using GeoLocator locator = new(_options);

        await Updater(http, locator).RunOnceAsync();

        Assert.Equal([CurrentMonthUrl, PreviousMonthUrl], http.Requests);
        Assert.True(locator.IsAvailable);
    }

    [Fact]
    public async Task Keeps_the_existing_database_when_the_current_month_is_not_published()
    {
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        ScriptedHttpHandler http = new(_ => ScriptedHttpHandler.NotFound());

        using GeoLocator locator = new(_options);

        await Updater(http, locator).RunOnceAsync();

        Assert.Equal([CurrentMonthUrl], http.Requests);
        Assert.True(locator.IsAvailable);
        Assert.False(File.Exists(TmpPath));
    }

    [Fact]
    public async Task A_corrupt_database_is_deleted_and_the_existing_one_is_kept()
    {
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        ScriptedHttpHandler http = new(_ =>
            ScriptedHttpHandler.Ok(Gzip([.. "not a MaxMind database"u8]))
        );

        using GeoLocator locator = new(_options);

        await Assert.ThrowsAsync<InvalidDatabaseException>(() => Updater(http, locator).RunOnceAsync());

        Assert.True(locator.IsAvailable);
        Assert.False(File.Exists(TmpPath));
    }

    [Fact]
    public async Task An_archive_that_is_not_gzip_is_deleted_and_the_existing_database_is_kept()
    {
        File.Copy(Fixture, _options.GeoIpDatabasePath);

        ScriptedHttpHandler http = new(_ =>
            ScriptedHttpHandler.Ok([.. "<html>maintenance</html>"u8])
        );

        using GeoLocator locator = new(_options);

        await Assert.ThrowsAsync<InvalidDataException>(() => Updater(http, locator).RunOnceAsync());

        Assert.True(locator.IsAvailable);
        Assert.False(File.Exists(TmpPath));
    }

    private string TmpPath => _options.GeoIpDatabasePath + ".tmp";

    private GeoIpUpdater Updater(ScriptedHttpHandler http, GeoLocator locator)
    {
        return new GeoIpUpdater(new AxiosClient(new AxiosRequestConfig(), http), locator, _options, _time);
    }

    private static byte[] GzippedFixture()
    {
        return Gzip(File.ReadAllBytes(Fixture));
    }

    private static byte[] Gzip(byte[] content)
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionMode.Compress))
        {
            gzip.Write(content);
        }

        return output.ToArray();
    }
}
