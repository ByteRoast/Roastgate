namespace Roastgate.Core.Nginx.Whitelists;

/// <summary>One <c>allow &lt;address&gt;;</c> line. The address is an IPv4 or IPv6 address, with an optional /prefix.</summary>
public sealed record WhitelistEntry(string Address, string? Comment = null);

/// <summary>What the structured editor understands: a leading comment block, then <c>allow</c> lines.</summary>
public sealed record WhitelistDocument(string? Header, IReadOnlyList<WhitelistEntry> Entries);

/// <param name="Document">The structured view, or null when the file holds anything else than <c>allow</c> lines.</param>
/// <param name="Unsupported">Why the file cannot be edited as a list (empty when it can).</param>
public sealed record WhitelistParseResult(WhitelistDocument? Document, IReadOnlyList<string> Unsupported);

public enum ProblemSeverity
{
    /// <summary>nginx accepts it, but it is probably not what was meant.</summary>
    Warning,

    /// <summary>nginx would refuse it.</summary>
    Error,
}

public sealed record WhitelistProblem(ProblemSeverity Severity, int? EntryIndex, string Message);

/// <summary>A config file that includes a whitelist.</summary>
/// <param name="Area">Where the including file lives.</param>
/// <param name="FileName">Its name in that area.</param>
/// <param name="FileEnabled">False when the including file is a disabled proxy or a site that is not enabled (so it has no effect now).</param>
/// <param name="Line">1-based line of the <c>include</c>.</param>
/// <param name="ViaWildcard">The include is a pattern such as <c>whitelists/*.conf</c> that happens to match.</param>
public sealed record WhitelistUsage(ConfigArea Area, string FileName, bool FileEnabled, int Line, bool ViaWildcard);

public sealed record WhitelistSummary(
    string Name,
    int? EntryCount,
    IReadOnlyList<WhitelistUsage> Usage,
    DateTimeOffset ModifiedAt);

public sealed record WhitelistDetails(
    string Name,
    string RawContent,
    WhitelistParseResult Parse,
    IReadOnlyList<WhitelistUsage> Usage,
    string IncludeSnippet,
    DateTimeOffset ModifiedAt);
