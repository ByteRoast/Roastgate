using System.Text.RegularExpressions;

namespace Roastgate.Core.Nginx;

public static partial class ConfigNames
{
    public const int MaximumLength = 100;
    public const long MaximumContentBytes = 1024 * 1024;
    public const string DisabledSuffix = ".disabled";
    public const string ConfigSuffix = ".conf";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
    private static partial Regex ValidName();

    /// <summary>
    /// Returns the file name to use for <paramref name="name"/> in <paramref name="area"/>,
    /// or null when it is not acceptable. Reverse proxies and whitelists must end with <c>.conf</c>
    /// (it is appended when missing).
    /// </summary>
    public static string? Normalize(ConfigArea area, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string trimmed = name.Trim();

        if (!IsValid(trimmed))
        {
            return null;
        }

        if (area != ConfigArea.Sites && !trimmed.EndsWith(ConfigSuffix, StringComparison.Ordinal))
        {
            trimmed += ConfigSuffix;
        }

        return IsValid(trimmed) ? trimmed : null;
    }

    public static bool IsValid(string name)
    {
        return name.Length <= MaximumLength
               && ValidName().IsMatch(name)
               && !name.Contains("..", StringComparison.Ordinal)
               && !name.EndsWith(DisabledSuffix, StringComparison.Ordinal)
               && !name.EndsWith(".tmp-roastgate", StringComparison.Ordinal);
    }
}
