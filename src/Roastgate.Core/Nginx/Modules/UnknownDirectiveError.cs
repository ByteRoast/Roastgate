using System.Text.RegularExpressions;

namespace Roastgate.Core.Nginx.Modules;

/// <summary>Reads the error nginx prints when a configuration names a directive the build does not have.</summary>
public static partial class UnknownDirectiveError
{
    [GeneratedRegex(
        """
        unknown directive "(?<name>[^"]+)"
        """
    )]
    private static partial Regex UnknownDirective();

    /// <summary>
    /// The directive named in the <c>unknown directive</c> error, or null when nginx failed for another reason (an
    /// invalid value, a module that cannot be loaded, a timeout). nginx stops at the first unknown directive, so there
    /// is at most one.
    /// </summary>
    /// <param name="output">Both streams of <c>nginx -t</c>: the error is printed on stderr.</param>
    public static string? Find(string output)
    {
        Match match = UnknownDirective().Match(output);

        return match.Success
            ? match.Groups["name"].Value
            : null;
    }
}
