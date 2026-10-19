using System.Text.RegularExpressions;

namespace Roastgate.Core.Nginx.Generation;

/// <summary>
/// Every value of a spec ends up in an nginx configuration file, so each one is checked against a strict pattern.
/// Only the two "extra directives" fields accept free text, and those must keep their braces balanced so that they
/// cannot close the block they are placed in.
/// </summary>
public static partial class SiteSpecValidator
{
    public const int MaximumServerNames = 20;
    public const int MaximumExtraLength = 4000;
    public const int MaximumRedirectUrlLength = 2000;

    [GeneratedRegex(
        @"^(_|(\*\.)?[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*)\z")]
    private static partial Regex ServerNamePattern();

    [GeneratedRegex(
        @"^(?<scheme>https?)://(?<host>[A-Za-z0-9_]([A-Za-z0-9._-]*[A-Za-z0-9_])?|\[[0-9A-Fa-f:.]+\])(:(?<port>\d{1,5}))?(?<path>/[A-Za-z0-9._~%/+=:@-]*)?\z")]
    private static partial Regex UpstreamPattern();

    // Same shape as UpstreamPattern, plus a query string: the URL ends up unquoted in "return 302 <url>;".
    [GeneratedRegex(
        @"^(?<scheme>https?)://(?<host>[A-Za-z0-9_]([A-Za-z0-9._-]*[A-Za-z0-9_])?|\[[0-9A-Fa-f:.]+\])(:(?<port>\d{1,5}))?(?<path>/[A-Za-z0-9._~%/+=:@-]*)?(?<query>\?[A-Za-z0-9._~%/+=:@&?-]*)?\z")]
    private static partial Regex RedirectUrlPattern();

    [GeneratedRegex(@"^/[A-Za-z0-9._/-]+\z")]
    private static partial Regex AbsolutePathPattern();

    [GeneratedRegex(@"^(0|[1-9]\d{0,5}[kKmMgG]?)\z")]
    private static partial Regex SizePattern();

    public static IReadOnlyList<SpecProblem> Validate(SiteSpec spec)
    {
        List<SpecProblem> problems = [];

        ValidateServerNames(spec, problems);
        ValidatePort(nameof(SiteSpec.HttpPort), spec.HttpPort, problems);

        if (spec.Kind == SiteKind.ReverseProxy)
        {
            ValidateUpstream(spec, problems);

            if (spec.ProxyTimeoutSeconds is { } timeout && (timeout < 1 || timeout > 3600))
            {
                problems.Add(new SpecProblem(nameof(SiteSpec.ProxyTimeoutSeconds),
                    "The timeout must be between 1 and 3600 seconds."));
            }
        }
        else if (!IsAbsolutePath(spec.RootDirectory))
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.RootDirectory),
                "The root directory must be an absolute path (letters, digits, dots, hyphens, underscores and slashes)."));
        }

        if (spec.EnableTls)
        {
            ValidateTls(spec, problems);
        }
        else if (spec.ForceHttps && spec.Hsts)
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.Hsts), "HSTS needs HTTPS to be enabled."));
        }

        if (!string.IsNullOrWhiteSpace(spec.MaxBodySize) && !SizePattern().IsMatch(spec.MaxBodySize))
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.MaxBodySize),
                "The maximum upload size must look like 100m, 512k or 2g (0 disables the limit)."));
        }

        foreach (string whitelist in spec.Whitelists)
        {
            if (ConfigNames.Normalize(ConfigArea.Whitelists, whitelist) != whitelist)
            {
                problems.Add(new SpecProblem(nameof(SiteSpec.Whitelists),
                    $"\"{whitelist}\" is not a valid whitelist file name."));
            }
        }

        ValidateDeniedBehavior(spec, problems);

        ValidateExtra(nameof(SiteSpec.ExtraServerDirectives), spec.ExtraServerDirectives, problems);
        ValidateExtra(nameof(SiteSpec.ExtraLocationDirectives), spec.ExtraLocationDirectives, problems);

        return problems;
    }

    private static void ValidateServerNames(SiteSpec spec, List<SpecProblem> problems)
    {
        if (spec.ServerNames.Count == 0)
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.ServerNames), "Enter at least one domain name."));
            return;
        }

        if (spec.ServerNames.Count > MaximumServerNames)
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.ServerNames),
                $"At most {MaximumServerNames} domain names are allowed."));
        }

        foreach (string name in spec.ServerNames)
        {
            if (name.Length > 253 || !ServerNamePattern().IsMatch(name))
            {
                problems.Add(new SpecProblem(nameof(SiteSpec.ServerNames),
                    $"\"{name}\" is not a valid domain name (use letters, digits and hyphens; punycode for international names)."));
            }
        }

        string? duplicate = spec.ServerNames
            .GroupBy((string name) => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault((IGrouping<string, string> group) => group.Count() > 1)?.Key;

        if (duplicate is not null)
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.ServerNames), $"\"{duplicate}\" is listed twice."));
        }
    }

    private static void ValidateUpstream(SiteSpec spec, List<SpecProblem> problems)
    {
        Match match = UpstreamPattern().Match(spec.Upstream);

        if (!match.Success)
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.Upstream),
                "The destination must look like http://127.0.0.1:3000 or https://backend:8443 (no spaces, quotes or semicolons)."));
            return;
        }

        if (match.Groups["port"].Success &&
            (!int.TryParse(match.Groups["port"].Value, out int port) || port < 1 || port > 65535))
        {
            problems.Add(
                new SpecProblem(nameof(SiteSpec.Upstream), "The destination port must be between 1 and 65535."));
        }

        if (match.Groups["path"].Value.Contains("..", StringComparison.Ordinal))
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.Upstream), "The destination path must not contain \"..\"."));
        }

        // With a path in proxy_pass, nginx replaces the matched location prefix by that path: "/app" turns /x into /appx.
        string destinationPath = match.Groups["path"].Value;

        if (destinationPath.Length > 0 && !destinationPath.EndsWith('/'))
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.Upstream),
                "A destination path must end with a slash (http://backend/app/), otherwise nginx glues it to the requested path."));
        }
    }

    private static void ValidateDeniedBehavior(SiteSpec spec, List<SpecProblem> problems)
    {
        if (spec.DeniedBehavior == DeniedBehavior.Forbidden)
        {
            return;
        }

        if (spec.Whitelists.Count == 0)
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.DeniedBehavior),
                "Choose at least one whitelist: without one, nobody is refused and this setting has no effect."));
            return;
        }

        // The URL of the other behaviors is ignored by the generator, so it is not checked.
        if (spec.DeniedBehavior != DeniedBehavior.Redirect)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(spec.DeniedUrl))
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.DeniedUrl),
                "Enter the address to redirect refused visitors to."));
            return;
        }

        Match match = spec.DeniedUrl.Length > MaximumRedirectUrlLength
            ? Match.Empty
            : RedirectUrlPattern().Match(spec.DeniedUrl);

        if (!match.Success)
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.DeniedUrl),
                $"The address must look like https://example.com/page?from=app, at most {MaximumRedirectUrlLength} characters (no spaces, quotes, semicolons, braces or $)."));
            return;
        }

        if (match.Groups["port"].Success &&
            (!int.TryParse(match.Groups["port"].Value, out int port) || port < 1 || port > 65535))
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.DeniedUrl),
                "The port of the address must be between 1 and 65535."));
        }
    }

    private static void ValidateTls(SiteSpec spec, List<SpecProblem> problems)
    {
        ValidatePort(nameof(SiteSpec.HttpsPort), spec.HttpsPort, problems);

        if (spec.HttpsPort == spec.HttpPort)
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.HttpsPort), "The HTTP and HTTPS ports must be different."));
        }

        if (!IsAbsolutePath(spec.CertificatePath))
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.CertificatePath),
                "The certificate must be an absolute path to a fullchain file."));
        }

        if (!IsAbsolutePath(spec.CertificateKeyPath))
        {
            problems.Add(new SpecProblem(nameof(SiteSpec.CertificateKeyPath),
                "The private key must be an absolute path."));
        }
    }

    private static void ValidatePort(string field, int port, List<SpecProblem> problems)
    {
        if (port < 1 || port > 65535)
        {
            problems.Add(new SpecProblem(field, "A port must be between 1 and 65535."));
        }
    }

    private static void ValidateExtra(string field, string? text, List<SpecProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.Length > MaximumExtraLength)
        {
            problems.Add(new SpecProblem(field, $"At most {MaximumExtraLength} characters are allowed."));
            return;
        }

        if (text.Any((char character) =>
                char.IsControl(character) && character != '\n' && character != '\r' && character != '\t'))
        {
            problems.Add(new SpecProblem(field, "Control characters are not allowed."));
            return;
        }

        if (!BracesBalanced(text))
        {
            problems.Add(new SpecProblem(field,
                "The braces do not balance: a } closes a block that was not opened here, or a { is never closed."));
        }
    }

    private static bool IsAbsolutePath(string? path)
    {
        return !string.IsNullOrEmpty(path) && AbsolutePathPattern().IsMatch(path) &&
               !path.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when every <c>{</c> is closed and no <c>}</c> closes more than was opened. Quoted strings and comments are skipped,
    /// as nginx does, so a brace inside them does not count.
    /// </summary>
    public static bool BracesBalanced(string text)
    {
        int depth = 0;
        char? quote = null;
        bool inComment = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];

            if (inComment)
            {
                inComment = character != '\n';
                continue;
            }

            if (quote is not null)
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == quote)
                {
                    quote = null;
                }

                continue;
            }

            switch (character)
            {
                case '#':
                    inComment = true;
                    break;
                case '"' or '\'':
                    quote = character;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth < 0)
                    {
                        return false;
                    }

                    break;
            }
        }

        return depth == 0 && quote is null;
    }
}
