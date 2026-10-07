namespace Roastgate.Web.Authentication;

public static class LocalUrl
{
    /// <summary>Returns <paramref name="url"/> when it is a path on this site, otherwise <paramref name="fallback"/> (prevents open redirects).</summary>
    public static string OrDefault(string? url, string fallback)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return fallback;
        }

        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
        {
            return fallback;
        }

        return url.Any(char.IsControl) ? fallback : url;
    }
}
