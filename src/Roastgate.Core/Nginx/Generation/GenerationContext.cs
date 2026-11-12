namespace Roastgate.Core.Nginx.Generation;

/// <param name="Locations">Where the whitelists live, to build the include lines.</param>
/// <param name="AcmeWebroot">The Let's Encrypt challenge directory from the settings.</param>
/// <param name="FileName">
/// The config file name, used to give the websocket map variable a name that is unique across files.
/// </param>
/// <param name="NginxVersion">
/// The version running on the host, when known. Directives that changed between versions depend on it.
/// </param>
public sealed record GenerationContext(
    ConfigLocations Locations,
    string AcmeWebroot,
    string FileName,
    Version? NginxVersion = null
)
{
    /// <summary>
    /// nginx 1.25.1 replaced the <c>http2</c> parameter of <c>listen</c> by a separate <c>http2 on;</c> directive,
    /// which older versions reject. The parameter form is accepted everywhere (newer versions only warn),
    /// so it is also used when the version is unknown.
    /// </summary>
    public bool UsesHttp2Directive
        => NginxVersion is { } version && version >= new Version(1, 25, 1);
}
