namespace Roastgate.Core.Nginx.Whitelists;

public sealed record WhitelistSummary(
    string Name,
    int? EntryCount,
    IReadOnlyList<WhitelistUsage> Usage,
    DateTimeOffset ModifiedAt
);
