namespace Roastgate.Core.GeoIP;

public sealed record IpLocation(string? City, string? Country, double? Latitude, double? Longitude);
