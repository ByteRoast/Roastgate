namespace Roastgate.Core.Certificates;

/// <param name="Unreadable">Names of directories that look like certificates but could not be read.</param>
public sealed record CertificateListing(
    IReadOnlyList<CertificateEntry> Certificates,
    string Directory,
    bool DirectoryExists,
    IReadOnlyList<string> Unreadable
);
