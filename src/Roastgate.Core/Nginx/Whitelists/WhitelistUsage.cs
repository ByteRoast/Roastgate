namespace Roastgate.Core.Nginx.Whitelists;

/// <summary>A config file that includes a whitelist.</summary>
/// <param name="Area">Where the including file lives.</param>
/// <param name="FileName">Its name in that area.</param>
/// <param name="FileEnabled">
/// False when the including file is a disabled proxy or a site that is not enabled (so it has no effect now).
/// </param>
/// <param name="Line">1-based line of the <c>include</c>.</param>
/// <param name="ViaWildcard">The include is a pattern such as <c>whitelists/*.conf</c> that happens to match.</param>
public sealed record WhitelistUsage(ConfigArea Area, string FileName, bool FileEnabled, int Line, bool ViaWildcard);
