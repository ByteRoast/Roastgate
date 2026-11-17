using System.Net;
using Roastgate.Core.GeoIP;

namespace Roastgate.Core.AccessLogs;

public sealed class TrafficMap(AccessLogReader reader, GeoLocator geoLocator)
{
    public async Task<TrafficSnapshot> BuildAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        Dictionary<IPAddress, int> requestsByIpAddress = [];

        await foreach (AccessLogEntry accessLogEntry in reader.ReadAsync(since, cancellationToken))
        {
            // requests per ip address
            requestsByIpAddress[accessLogEntry.Client] = requestsByIpAddress.GetValueOrDefault(
                accessLogEntry.Client
            ) + 1;
        }

        Dictionary<(double Latitude, double Longitude), TrafficPoint> pointsByPosition = [];

        int unlocatedRequests = 0;

        foreach ((IPAddress ipAddress, int requests) in requestsByIpAddress)
        {
            IpLocation? location = geoLocator.Locate(ipAddress);

            if (location?.Latitude is not { } latitude || location.Longitude is not { } longitude)
            {
                // private or unknown or absent IP
                unlocatedRequests += requests;
                continue;
            }

            (double, double) position = (latitude, longitude);

            pointsByPosition[position] = pointsByPosition.TryGetValue(position, out TrafficPoint? point)
                ? point with
                {
                    Requests = point.Requests + requests,
                    Addresses = point.Addresses + 1
                }
                : new TrafficPoint(
                    latitude,
                    longitude,
                    location.City,
                    location.Country,
                    requests,
                    1
                );
        }

        return new TrafficSnapshot([.. pointsByPosition.Values], unlocatedRequests);
    }
}
