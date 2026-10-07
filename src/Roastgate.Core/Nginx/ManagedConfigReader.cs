using Roastgate.Core.Settings;

namespace Roastgate.Core.Nginx;

/// <summary>A reverse proxy or a site, with its lines, as seen by the scanners that look for includes and certificates.</summary>
/// <param name="Enabled">False for a disabled proxy or a site that is not enabled: nginx does not read it.</param>
public sealed record ManagedConfig(ConfigArea Area, string FileName, bool Enabled, string[] Lines);

public interface IManagedConfigReader
{
    /// <summary>Every reverse proxy (conf.d) and site (sites-available) that Roastgate manages.</summary>
    Task<IReadOnlyList<ManagedConfig>> ReadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class ManagedConfigReader(AppSettingsService settingsService) : IManagedConfigReader
{
    public async Task<IReadOnlyList<ManagedConfig>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        ConfigLocations locations = new(await settingsService.GetAsync(cancellationToken));
        List<ManagedConfig> configs = [];

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
                if (!isManagedName || new FileInfo(path).Length > ConfigNames.MaximumContentBytes)
                {
                    continue;
                }

                bool enabled = area == ConfigArea.ReverseProxies
                    ? !disabledProxy
                    : ConfigStore.IsLinkedTo(Path.Combine(locations.SitesEnabledDirectory, name), path);
                string content = await File.ReadAllTextAsync(path, cancellationToken);
                configs.Add(new ManagedConfig(area, name, enabled, content.Replace("\r\n", "\n").Split('\n')));
            }
        }

        return configs;
    }
}

/// <summary>Path helpers for comparing the paths that appear in nginx configs.</summary>
public static class NginxPaths
{
    /// <summary>
    /// A comparable form of an nginx path: forward slashes, no "." or ".." segments, absolute (relative paths are
    /// resolved against the nginx directory, as nginx does).
    /// </summary>
    public static string Normalize(string path, string nginxRoot)
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

    public static string Unquote(string text)
    {
        return text.Length >= 2 && (text[0] == '"' || text[0] == '\'') ? text[1..^1] : text;
    }
}
