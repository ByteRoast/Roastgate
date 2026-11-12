namespace Roastgate.Core.Nginx.Modules;

/// <summary>The nginx block a directive is tested in by the capability probe.</summary>
public enum DirectiveContext
{
    Http,
    Server,
    Location,
}
