namespace Roastgate.Core.Security;

/// <summary>
/// Where the browser is: a passkey is bound to a host name (<see cref="Id"/>) and only works on the origin it
/// was made on.
/// </summary>
public sealed record PasskeyRelyingParty(string Id, string Origin)
{
    public static PasskeyRelyingParty FromUri(Uri uri)
    {
        return new PasskeyRelyingParty(uri.Host, uri.GetLeftPart(UriPartial.Authority));
    }
}
