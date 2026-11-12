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
