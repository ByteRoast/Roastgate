namespace Roastgate.Core.AccessLogs;

public sealed record TrafficPoint(
    double Latitude,
    double Longitude,
    string? City,
    string? Country,
    string? CountryCode,
    int Requests,
    int Addresses
);
