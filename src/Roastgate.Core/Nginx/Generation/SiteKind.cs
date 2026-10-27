namespace Roastgate.Core.Nginx.Generation;

public enum SiteKind
{
    /// <summary>Forwards requests to another server (an application, a container).</summary>
    ReverseProxy,

    /// <summary>Serves files from a directory.</summary>
    StaticSite,
}
