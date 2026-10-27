namespace Roastgate.Core.Nginx.Whitelists;

/// <summary>What the structured editor understands: a leading comment block, then <c>allow</c> lines.</summary>
public sealed record WhitelistDocument(string? Header, IReadOnlyList<WhitelistEntry> Entries);
