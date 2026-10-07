using System.Text.RegularExpressions;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Nginx;

/// <summary>The three families of files Roastgate manages.</summary>
public enum ConfigArea
{
    /// <summary>Reverse proxies: <c>conf.d/*.conf</c>, disabled by renaming to <c>*.conf.disabled</c>.</summary>
    ReverseProxies,

    /// <summary>Hosted sites: <c>sites-available/*</c>, enabled by a symlink in <c>sites-enabled</c>.</summary>
    Sites,

    /// <summary>IP whitelists: <c>whitelists/*.conf</c>, included from other configs.</summary>
    Whitelists,
}

/// <param name="Wizard">The file carries the wizard's header, so the wizard can reopen it.</param>
public sealed record ConfigEntry(string Name, ConfigArea Area, bool Enabled, long SizeBytes, DateTimeOffset ModifiedAt, bool Wizard = false);

public sealed record ConfigFile(string Name, ConfigArea Area, string Content, bool Enabled, DateTimeOffset ModifiedAt);

public sealed record ConfigListing(IReadOnlyList<ConfigEntry> Entries, string Directory, bool DirectoryExists);

/// <param name="Succeeded">The requested change is in place.</param>
/// <param name="Message">A sentence for the user.</param>
/// <param name="NginxOutput">What nginx printed, when it was involved.</param>
/// <param name="RolledBack">The change was refused by nginx and undone.</param>
/// <param name="Reloaded">nginx was asked to reload and accepted.</param>
/// <param name="Warning">The change is in place but something needs the user's attention (for example the reload failed).</param>
public sealed record ConfigResult(
    bool Succeeded,
    string Message,
    string? NginxOutput = null,
    bool RolledBack = false,
    bool Reloaded = false,
    bool Warning = false);

public static partial class ConfigNames
{
    public const int MaximumLength = 100;
    public const long MaximumContentBytes = 1024 * 1024;

    public const string DisabledSuffix = ".disabled";
    public const string ConfigSuffix = ".conf";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
    private static partial Regex ValidName();

    /// <summary>
    /// Returns the file name to use for <paramref name="name"/> in <paramref name="area"/>, or null when it is not acceptable.
    /// Reverse proxies and whitelists must end with <c>.conf</c> (it is appended when missing).
    /// </summary>
    public static string? Normalize(ConfigArea area, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string trimmed = name.Trim();
        if (!IsValid(trimmed))
        {
            return null;
        }

        if (area != ConfigArea.Sites && !trimmed.EndsWith(ConfigSuffix, StringComparison.Ordinal))
        {
            trimmed += ConfigSuffix;
        }

        return IsValid(trimmed) ? trimmed : null;
    }

    public static bool IsValid(string name)
    {
        return name.Length <= MaximumLength
               && ValidName().IsMatch(name)
               && !name.Contains("..", StringComparison.Ordinal)
               && !name.EndsWith(DisabledSuffix, StringComparison.Ordinal)
               && !name.EndsWith(".tmp-roastgate", StringComparison.Ordinal);
    }
}

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
