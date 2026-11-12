using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Roastgate.Core.Settings;

/// <summary>
/// Settings chosen during setup and editable later. Paths are validated because they end up in generated nginx configs.
/// </summary>
public sealed partial record AppSettings
{
    public string NginxRoot { get; init; } = "/etc/nginx";

    public string WhitelistsDirectory { get; init; } = "/etc/nginx/whitelists";

    public string AcmeWebroot { get; init; } = "/var/www/certbot";

    /// <summary>
    /// Where Let's Encrypt keeps certificates (<c>live/&lt;name&gt;/fullchain.pem</c>). Shared with the host when it
    /// is mounted.
    /// </summary>
    public string LetsEncryptDirectory { get; init; } = "/etc/letsencrypt";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CertbotMode CertbotMode { get; init; } = CertbotMode.Embedded;

    public string? AcmeEmail { get; init; }

    [GeneratedRegex(@"^/[A-Za-z0-9._/-]*\z")]
    private static partial Regex AbsolutePathPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+\z")]
    private static partial Regex EmailPattern();

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        ValidatePath(problems, "nginx root", NginxRoot);
        ValidatePath(problems, "whitelists directory", WhitelistsDirectory);
        ValidatePath(problems, "ACME webroot", AcmeWebroot);
        ValidatePath(problems, "Let's Encrypt directory", LetsEncryptDirectory);

        if (!string.IsNullOrWhiteSpace(AcmeEmail) && !EmailPattern().IsMatch(AcmeEmail))
        {
            problems.Add("The Let's Encrypt e-mail address is not valid.");
        }

        return problems;
    }

    private static void ValidatePath(List<string> problems, string label, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length == 1 || !AbsolutePathPattern().IsMatch(path) ||
            path.Contains("..", StringComparison.Ordinal))
        {
            problems.Add(
                $"The {label} must be an absolute path (letters, digits, dots, hyphens, underscores and slashes only)."
            );
        }
    }
}
