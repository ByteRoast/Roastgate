using Roastgate.Core.Nginx;

namespace Roastgate.Web.Services;

/// <summary>How each config area is presented: titles and the (lowercase, hyphenated) routes of its pages.</summary>
public sealed record AreaInfo(ConfigArea Area, string Title, string Singular, string ListRoute, string EditRoute, string Description, string? WizardRoute = null, string Directory = "")
{
    public static AreaInfo For(ConfigArea area)
    {
        return area switch
        {
            ConfigArea.ReverseProxies => new AreaInfo(
                area,
                "Reverse proxies",
                "reverse proxy",
                "/reverse-proxies",
                "/reverse-proxies/edit",
                "Files in conf.d. Disabling one renames it to .conf.disabled so nginx stops reading it.",
                "/reverse-proxies/wizard",
                "conf.d"),
            ConfigArea.Sites => new AreaInfo(
                area,
                "Sites",
                "site",
                "/sites",
                "/sites/edit",
                "Files in sites-available. A site is enabled by a symlink in sites-enabled.",
                "/sites/wizard",
                "sites-available"),
            ConfigArea.Whitelists => new AreaInfo(
                area,
                "Whitelists",
                "whitelist",
                "/whitelists",
                "/whitelists/edit",
                "IP lists that your configs include. They apply wherever they are included."),
            _ => throw new ArgumentOutOfRangeException(nameof(area)),
        };
    }

    public string WizardUrl(string? name)
    {
        string route = WizardRoute ?? EditRoute;
        return name is null ? route : route + "?name=" + Uri.EscapeDataString(name);
    }

    public string BackupsUrl(string? name)
    {
        string url = "/backups?area=" + Area;
        return name is null ? url : url + "&name=" + Uri.EscapeDataString(name);
    }

    public string EditUrl(string? name)
    {
        return name is null ? EditRoute : EditRoute + "?name=" + Uri.EscapeDataString(name);
    }
}
