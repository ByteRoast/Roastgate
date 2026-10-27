namespace Roastgate.Core.Nginx.Whitelists;

/// <param name="Document">
/// The structured view, or null when the file holds anything else than <c>allow</c> lines.
/// </param>
/// <param name="Unsupported">Why the file cannot be edited as a list (empty when it can).</param>
public sealed record WhitelistParseResult(WhitelistDocument? Document, IReadOnlyList<string> Unsupported);
