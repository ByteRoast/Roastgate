namespace Roastgate.Core.Nginx.Generation;

public enum SiteKind
{
    /// <summary>Forwards requests to another server (an application, a container).</summary>
    ReverseProxy,

    /// <summary>Serves files from a directory.</summary>
    StaticSite,
}

/// <summary>What a visitor whose address is in none of the site's whitelists gets.</summary>
public enum DeniedBehavior
{
    /// <summary>403 Forbidden, nginx's own answer to <c>deny</c>.</summary>
    Forbidden,

    /// <summary>404 Not Found, which hides that the site exists.</summary>
    NotFound,

    /// <summary>Closes the connection without sending anything (444, specific to nginx).</summary>
    CloseConnection,

    /// <summary>Redirects (302) to <see cref="SiteSpec.DeniedUrl"/>.</summary>
    Redirect,
}

/// <summary>
/// Everything the wizard asks for. It is validated by <see cref="SiteSpecValidator"/> before anything reaches nginx,
/// and stored (as JSON, in a comment) in the generated file so that the wizard can reopen it later.
/// </summary>
public sealed record SiteSpec
{
    public SiteKind Kind { get; init; } = SiteKind.ReverseProxy;

    /// <summary>The <c>server_name</c> values: hostnames, a leading wildcard such as *.example.com, or _ for the default server.</summary>
    public IReadOnlyList<string> ServerNames { get; init; } = [];

    public int HttpPort { get; init; } = 80;

    /// <summary>Also listen on IPv6 (<c>listen [::]:port</c>). Turn it off on hosts without IPv6, where nginx refuses that line.</summary>
    public bool Ipv6 { get; init; } = true;

    // ---- Reverse proxy ----

    /// <summary>For example http://127.0.0.1:3000 or https://backend.internal:8443/app.</summary>
    public string Upstream { get; init; } = string.Empty;

    public bool Websockets { get; init; } = true;

    public int? ProxyTimeoutSeconds { get; init; }

    // ---- Static site ----

    public string RootDirectory { get; init; } = string.Empty;

    /// <summary>Unknown paths fall back to /index.html (single page applications).</summary>
    public bool SpaFallback { get; init; }

    public bool CacheStaticAssets { get; init; } = true;

    // ---- HTTPS ----

    public bool EnableTls { get; init; }

    public int HttpsPort { get; init; } = 443;

    public string CertificatePath { get; init; } = string.Empty;

    public string CertificateKeyPath { get; init; } = string.Empty;

    /// <summary>Redirect plain HTTP to HTTPS.</summary>
    public bool ForceHttps { get; init; } = true;

    public bool Http2 { get; init; } = true;

    /// <summary>Send Strict-Transport-Security (one year).</summary>
    public bool Hsts { get; init; }

    // ---- Common ----

    /// <summary>For example 100m. Null keeps nginx's default (1m).</summary>
    public string? MaxBodySize { get; init; }

    public bool SecurityHeaders { get; init; } = true;

    /// <summary>Refuse TRACE and TRACK, and dotfiles other than /.well-known.</summary>
    public bool BlockCommonExploits { get; init; } = true;

    /// <summary>Names of whitelists (for example alice.conf). Anyone not listed is refused, as <see cref="DeniedBehavior"/> says.</summary>
    public IReadOnlyList<string> Whitelists { get; init; } = [];

    /// <summary>How visitors outside the whitelists are refused. Only used when there is at least one whitelist.</summary>
    public DeniedBehavior DeniedBehavior { get; init; } = DeniedBehavior.Forbidden;

    /// <summary>Where <see cref="DeniedBehavior.Redirect"/> sends refused visitors, for example https://example.com/.</summary>
    public string? DeniedUrl { get; init; }

    /// <summary>Let the Let's Encrypt HTTP challenge through (the webroot comes from the settings), even behind a whitelist.</summary>
    public bool ServeAcmeChallenge { get; init; }

    /// <summary>Free nginx directives added inside the server block. Braces must balance.</summary>
    public string? ExtraServerDirectives { get; init; }

    /// <summary>Free nginx directives added inside the main location block. Braces must balance.</summary>
    public string? ExtraLocationDirectives { get; init; }
}

/// <param name="Field">The <see cref="SiteSpec"/> property the message is about.</param>
public sealed record SpecProblem(string Field, string Message);
