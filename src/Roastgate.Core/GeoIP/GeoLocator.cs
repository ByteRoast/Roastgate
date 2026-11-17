using System.Net;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Responses;

namespace Roastgate.Core.GeoIP;

public sealed class GeoLocator : IDisposable
{
    public bool IsAvailable
    {
        get
        {
            lock (_lock)
            {
                return _reader is not null;
            }
        }
    }

    public DateTime? BuildDate
    {
        get
        {
            lock (_lock)
            {
                return _reader?.Metadata.BuildDate;
            }
        }
    }

    private readonly Lock _lock = new();
    private readonly string _path;
    private DatabaseReader? _reader;

    public GeoLocator(RoastgateOptions options)
    {
        _path = options.GeoIpDatabasePath;

        if (File.Exists(_path))
        {
            _reader = new DatabaseReader(_path);
        }
    }

    public IpLocation? Locate(IPAddress ipAddress)
    {
        lock (_lock)
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
                response.Country.IsoCode,
                response.Location.Latitude,
                response.Location.Longitude
            );
        }
    }

    public void Replace(string newFilePath)
    {
        lock (_lock)
        {
            _reader?.Dispose();
            _reader = null;

            File.Move(newFilePath, _path, overwrite: true);

            _reader = new DatabaseReader(_path);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _reader?.Dispose();
        }
    }
}
