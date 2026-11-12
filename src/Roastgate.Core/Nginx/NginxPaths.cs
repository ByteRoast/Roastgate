namespace Roastgate.Core.Nginx;

/// <summary>Path helpers for comparing the paths that appear in nginx configs.</summary>
public static class NginxPaths
{
    /// <summary>
    /// A comparable form of an nginx path: forward slashes, no "." or ".." segments, absolute (relative paths are
    /// resolved against the nginx directory, as nginx does).
    /// </summary>
    public static string Normalize(string path, string nginxRoot)
    {
        string unified = path.Replace('\\', '/');
        string absolute = unified.StartsWith('/') || (unified.Length > 1 && unified[1] == ':')
            ? unified
            : nginxRoot.Replace('\\', '/').TrimEnd('/') + "/" + unified;

        List<string> segments = [];

        foreach (string segment in absolute.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        string joined = string.Join('/', segments);

        return absolute.StartsWith('/') ? "/" + joined : joined;
    }

    public static string Unquote(string text)
    {
        return text.Length >= 2 && (text[0] == '"' || text[0] == '\'') ? text[1..^1] : text;
    }
}
