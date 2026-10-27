namespace Roastgate.Core.Nginx.Whitelists;

public sealed record WhitelistDetails(
    string Name,
    string RawContent,
    WhitelistParseResult Parse,
    IReadOnlyList<WhitelistUsage> Usage,
    string IncludeSnippet,
    DateTimeOffset ModifiedAt
);
