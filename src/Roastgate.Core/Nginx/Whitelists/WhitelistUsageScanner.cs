using System.Text.RegularExpressions;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Nginx.Whitelists;

public interface IWhitelistUsageScanner
{
    /// <summary>Every include, in the managed configs, that pulls in each of the given whitelists.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<WhitelistUsage>>> FindAsync(IReadOnlyCollection<string> whitelistNames, CancellationToken cancellationToken = default);
}

/// <summary>
/// Finds where whitelists are included by reading the reverse proxies (conf.d) and the sites (sites-available).
/// It understands quoted and relative paths and wildcard includes. It does not read nginx.conf or files that
/// those configs include in turn, so "not used" means "not used by anything Roastgate manages".
/// </summary>
public sealed partial class WhitelistUsageScanner(AppSettingsService settingsService) : IWhitelistUsageScanner
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

        foreach (ConfigArea area in new[] { ConfigArea.ReverseProxies, ConfigArea.Sites })
        {
            string directory = locations.Directory(area);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(path);
                bool disabledProxy = area == ConfigArea.ReverseProxies && fileName.EndsWith(ConfigNames.DisabledSuffix, StringComparison.Ordinal);
                string name = disabledProxy ? fileName[..^ConfigNames.DisabledSuffix.Length] : fileName;
                bool isManagedName = ConfigNames.IsValid(name) && (area == ConfigArea.Sites || name.EndsWith(ConfigNames.ConfigSuffix, StringComparison.Ordinal));
                if (!isManagedName)
                {
                    continue;
                }

                bool enabled = area == ConfigArea.ReverseProxies
                    ? !disabledProxy
                    : ConfigStore.IsLinkedTo(Path.Combine(locations.SitesEnabledDirectory, name), path);

                string[] lines = await ReadLinesAsync(path, cancellationToken);
                for (int index = 0; index < lines.Length; index++)
                {
                    Match match = IncludeStatement().Match(lines[index]);
                    if (!match.Success)
                    {
                        continue;
                    }

                    string target = NormalizePath(Unquote(match.Groups["path"].Value), settings.NginxRoot);
                    bool isPattern = target.Contains('*', StringComparison.Ordinal) || target.Contains('?', StringComparison.Ordinal);
                    foreach (KeyValuePair<string, string> whitelist in whitelistPaths)
                    {
                        bool matches = isPattern ? GlobMatches(target, whitelist.Value) : string.Equals(target, whitelist.Value, StringComparison.Ordinal);
                        if (matches)
                        {
                            usage[whitelist.Key].Add(new WhitelistUsage(area, name, enabled, index + 1, isPattern));
                        }
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

    private static async Task<string[]> ReadLinesAsync(string path, CancellationToken cancellationToken)
    {
        FileInfo info = new(path);
        if (info.Length > ConfigNames.MaximumContentBytes)
        {
            return [];
        }

        return (await File.ReadAllTextAsync(path, cancellationToken)).Replace("\r\n", "\n").Split('\n');
    }

    private static string Unquote(string text)
    {
        return text.Length >= 2 && (text[0] == '"' || text[0] == '\'') ? text[1..^1] : text;
    }

    /// <summary>
    /// A comparable form of an nginx path: forward slashes, no "." or ".." segments, absolute (relative paths are
    /// resolved against the nginx directory, as nginx does).
    /// </summary>
    internal static string NormalizePath(string path, string nginxRoot)
    {
        string unified = path.Replace('\\', '/');
        string absolute = unified.StartsWith('/') || (unified.Length > 1 && unified[1] == ':')
            ? unified
            : nginxRoot.Replace('\\', '/').TrimEnd('/') + "/" + unified;

        List<string> segments = [];
        foreach (string segment in absolute.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        string joined = string.Join('/', segments);
        return absolute.StartsWith('/') ? "/" + joined : joined;
    }

    private static bool GlobMatches(string pattern, string path)
    {
        string expression = "^" + Regex.Escape(pattern).Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$";
        return Regex.IsMatch(path, expression, RegexOptions.CultureInvariant);
    }
}
