namespace Roastgate.Core.Nginx;

public sealed record ConfigListing(IReadOnlyList<ConfigEntry> Entries, string Directory, bool DirectoryExists);
