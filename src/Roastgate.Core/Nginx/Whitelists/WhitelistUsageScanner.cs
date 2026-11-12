using System.Text.RegularExpressions;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Nginx.Whitelists;

/// <summary>
/// Finds where whitelists are included by reading the reverse proxies (conf.d) and the sites (sites-available).
/// It understands quoted and relative paths and wildcard includes. It does not read nginx.conf or files that
/// those configs include in turn, so "not used" means "not used by anything Roastgate manages".
/// </summary>
public sealed partial class WhitelistUsageScanner(IManagedConfigReader configs, AppSettingsService settingsService) : IWhitelistUsageScanner
{
    [GeneratedRegex(@"^\s*include\s+(?<path>""[^""]*""|'[^']*'|[^\s;]+)\s*;")]
    private static partial Regex IncludeStatement();

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<WhitelistUsage>>> FindAsync(
        IReadOnlyCollection<string> whitelistNames,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        ConfigLocations locations = new(settings);
        Dictionary<string, List<WhitelistUsage>> usage = whitelistNames.ToDictionary((string name) => name, (string _) => new List<WhitelistUsage>(), StringComparer.Ordinal);
        Dictionary<string, string> whitelistPaths = whitelistNames.ToDictionary(
            (string name) => name,
            (string name) => NormalizePath(locations.WhitelistIncludePath(name), settings.NginxRoot),
            StringComparer.Ordinal);

        foreach (ManagedConfig config in await configs.ReadAllAsync(cancellationToken))
        {
            for (int index = 0; index < config.Lines.Length; index++)
            {
                Match match = IncludeStatement().Match(config.Lines[index]);
                if (!match.Success)
                {
                    continue;
                }

                string target = NormalizePath(NginxPaths.Unquote(match.Groups["path"].Value), settings.NginxRoot);
                bool isPattern = target.Contains('*', StringComparison.Ordinal) || target.Contains('?', StringComparison.Ordinal);
                foreach (KeyValuePair<string, string> whitelist in whitelistPaths)
                {
                    bool matches = isPattern ? GlobMatches(target, whitelist.Value) : string.Equals(target, whitelist.Value, StringComparison.Ordinal);
                    if (matches)
                    {
                        usage[whitelist.Key].Add(new WhitelistUsage(config.Area, config.FileName, config.Enabled, index + 1, isPattern));
                    }
                }
            }
        }

        return usage.ToDictionary(
            (KeyValuePair<string, List<WhitelistUsage>> pair) => pair.Key,
            (KeyValuePair<string, List<WhitelistUsage>> pair) => (IReadOnlyList<WhitelistUsage>)pair.Value
                .OrderBy((WhitelistUsage item) => item.Area)
                .ThenBy((WhitelistUsage item) => item.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy((WhitelistUsage item) => item.Line)
                .ToList(),
            StringComparer.Ordinal);
    }

    internal static string NormalizePath(string path, string nginxRoot)
    {
        return NginxPaths.Normalize(path, nginxRoot);
    }

    private static bool GlobMatches(string pattern, string path)
    {
        string expression = "^" + Regex.Escape(pattern).Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$";
        return Regex.IsMatch(path, expression, RegexOptions.CultureInvariant);
    }
}
