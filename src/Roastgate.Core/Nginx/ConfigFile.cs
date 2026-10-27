namespace Roastgate.Core.Nginx;

public sealed record ConfigFile(string Name, ConfigArea Area, string Content, bool Enabled, DateTimeOffset ModifiedAt);
