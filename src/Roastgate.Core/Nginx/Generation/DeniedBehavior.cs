namespace Roastgate.Core.Nginx.Generation;

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
