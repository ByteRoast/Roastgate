using Roastgate.Core.Settings;

namespace Roastgate.Core.Nginx;

/// <summary>Where each area lives on disk, according to the settings chosen at setup.</summary>
public sealed class ConfigLocations(AppSettings settings)
{
    public string Directory(ConfigArea area)
    {
        return area switch
        {
            ConfigArea.ReverseProxies => Path.Combine(settings.NginxRoot, "conf.d"),
            ConfigArea.Sites => Path.Combine(settings.NginxRoot, "sites-available"),
            ConfigArea.Whitelists => settings.WhitelistsDirectory,
            _ => throw new ArgumentOutOfRangeException(nameof(area)),
        };
    }

    public string SitesEnabledDirectory => Path.Combine(settings.NginxRoot, "sites-enabled");

    /// <summary>The path nginx's <c>include</c> must use for a whitelist (the directory is mounted at the same path on the host).</summary>
    public string WhitelistIncludePath(string name)
    {
        return settings.WhitelistsDirectory.TrimEnd('/') + "/" + name;
    }
}
