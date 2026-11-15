using System.Net;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Responses;

namespace Roastgate.Core.GeoIP;

public sealed class GeoLocator : IDisposable
{
    public bool IsAvailable => _reader is not null;

    private readonly DatabaseReader? _reader;

    public GeoLocator(RoastgateOptions options)
    {
        string path = options.GeoIpDatabasePath;

        if (File.Exists(path))
        {
            _reader = new DatabaseReader(path);
        }
    }

    public IpLocation? Locate(IPAddress ipAddress)
    {
        if (_reader == null)
        {
            return null;
        }

        if (!_reader.TryCity(ipAddress, out CityResponse? response))
        {
            return null;
        }

        return new IpLocation(
            response.City.Name,
            response.Country.Name,
            response.Location.Latitude,
            response.Location.Longitude
        );
    }

    public void Dispose()
    {
        _reader?.Dispose();
    }
}
