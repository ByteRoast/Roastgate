using System.Net;
using Roastgate.Core;
using Roastgate.Core.GeoIP;

namespace Roastgate.Tests.GeoIP;

public sealed class GeoLocatorTests
{
    [Fact]
    public void Is_unavailable_when_the_database_file_is_missing()
    {
        using GeoLocator locator = new(OptionsWithoutDatabase());

        Assert.False(locator.IsAvailable);
    }

    [Fact]
    public void Locate_returns_null_when_the_database_file_is_missing()
    {
        using GeoLocator locator = new(OptionsWithoutDatabase());

        Assert.Null(locator.Locate(IPAddress.Parse("8.8.8.8")));
    }

    // The directory is never created: GeoLocator only reads, so there is nothing to clean up.
    private static RoastgateOptions OptionsWithoutDatabase()
    {
        return new RoastgateOptions
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "roastgate-geoip-" + Guid.NewGuid().ToString("N")),
        };
    }
}
