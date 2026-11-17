namespace Roastgate.Core.GeoIP;

public sealed record IpLocation(
    string? City,
    string? Country,
    string? CountryCode,
    double? Latitude,
    double? Longitude
);
