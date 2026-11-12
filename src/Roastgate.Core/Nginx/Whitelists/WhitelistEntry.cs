namespace Roastgate.Core.Nginx.Whitelists;

/// <summary>
/// One <c>allow &lt;address&gt;;</c> line. The address is an IPv4 or IPv6 address, with an optional /prefix.
/// </summary>
public sealed record WhitelistEntry(string Address, string? Comment = null);
