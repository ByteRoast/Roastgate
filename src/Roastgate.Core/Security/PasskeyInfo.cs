namespace Roastgate.Core.Security;

public sealed record PasskeyInfo(int Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
