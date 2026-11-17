namespace Roastgate.Core.AccessLogs;

public sealed record TrafficPoint(
    double Latitude,
    double Longitude,
    string? City,
    string? Country,
    int Requests,
    int Addresses
);
