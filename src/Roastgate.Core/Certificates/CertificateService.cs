using System.Text.RegularExpressions;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Hosting;
using Roastgate.Core.Nginx;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Certificates;

public interface ICertificateService
{
    Task<CertificateOperation> IssueAsync(
        IssueRequest request,
        string actor,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Renews what is due (or everything with <paramref name="force"/>) and reloads nginx when a certificate
    /// actually changed.
    /// </summary>
    Task<CertificateOperation> RenewAsync(
        string? name,
        bool force,
        string actor,
        CancellationToken cancellationToken = default
    );

    /// <summary>Refused while a config still points at the certificate.</summary>
    Task<CertificateOperation> DeleteAsync(string name, string actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Drives certbot, the embedded one (run inside the container, on the shared <c>/etc/letsencrypt</c>) or the one on the host.
/// Arguments are built here from validated values only, and never go through a shell.
/// </summary>
public sealed partial class CertificateService(
    AppSettingsService settingsService,
    RoastgateOptions options,
    IHostExec hostExec,
    ILocalExec localExec,
    ICertificateCatalog catalog,
    IConfigStore store,
    IAuditLog audit
) : ICertificateService
{
    public static readonly TimeSpan CertbotTimeout = TimeSpan.FromMinutes(5);

    private const int MaximumDomains = 20;
    private const int MaximumAuditDetailLength = 2000;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+\z")]
    private static partial Regex DomainPattern();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,60}\z")]
    private static partial Regex CertificateNamePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+\z")]
    private static partial Regex EmailPattern();

    public async Task<CertificateOperation> IssueAsync(
        IssueRequest request,
        string actor,
        CancellationToken cancellationToken = default
    )
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);

        string? problem = ValidateIssue(request, settings);

        if (problem is not null)
        {
            return new CertificateOperation(false, problem);
        }

        string name = (request.CertName?.Trim()).NullIfEmpty() ?? request.Domains[0].ToLowerInvariant();

        List<string> arguments =
        [
            "certonly", "--webroot", "-w", settings.AcmeWebroot,
            "--cert-name", name,
            "--non-interactive", "--agree-tos", "--no-eff-email", "--expand",
            "--preferred-challenges", "http",
        ];

        foreach (string domain in request.Domains)
        {
            arguments.Add("-d");
            arguments.Add(domain.ToLowerInvariant());
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            arguments.Add("--register-unsafely-without-email");
        }
        else
        {
            arguments.Add("--email");
            arguments.Add(request.Email.Trim());
        }

        if (request.Staging)
        {
            arguments.Add("--staging");
        }

        if (request.DryRun)
        {
            arguments.Add("--dry-run");
        }

        await Gate.WaitAsync(cancellationToken);

        try
        {
            HostCommandResult result = await RunCertbotAsync(settings, arguments, cancellationToken);

            string action = request.DryRun ? "certbot.dry-run" : "certbot.issue";

            await audit.WriteAsync(actor, action, name, result.Succeeded, Truncate(result.CombinedOutput),
                cancellationToken);

            if (!result.Succeeded)
            {
                return new CertificateOperation(false,
                    "certbot could not issue the certificate. The output below says why.", result.CombinedOutput);
            }

            string message = request.DryRun
                ? "The dry run succeeded: Let's Encrypt can validate your domains. Nothing was issued."
                : $"The certificate {name} was issued.";

            return new CertificateOperation(true, message, result.CombinedOutput);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<CertificateOperation> RenewAsync(
        string? name,
        bool force,
        string actor,
        CancellationToken cancellationToken = default
    )
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);

        if (settings.CertbotMode == CertbotMode.Disabled)
        {
            return new CertificateOperation(false, "Certificate management is disabled in the settings.");
        }

        if (name is not null && !CertificateNamePattern().IsMatch(name))
        {
            return new CertificateOperation(false, "That is not a valid certificate name.");
        }

        await Gate.WaitAsync(cancellationToken);

        try
        {
            Dictionary<string, DateTimeOffset> before = await SnapshotAsync(cancellationToken);

            if (name is not null && !before.ContainsKey(name))
            {
                return new CertificateOperation(false, $"There is no certificate named {name}.");
            }

            List<string> arguments = ["renew", "--non-interactive"];

            if (name is not null)
            {
                arguments.Add("--cert-name");
                arguments.Add(name);
            }

            if (force)
            {
                arguments.Add("--force-renewal");
            }

            HostCommandResult result = await RunCertbotAsync(settings, arguments, cancellationToken);
            Dictionary<string, DateTimeOffset> after = await SnapshotAsync(cancellationToken);

            List<string> renewed = after
                .Where((KeyValuePair<string, DateTimeOffset> pair) =>
                    !before.TryGetValue(pair.Key, out DateTimeOffset previous) || previous != pair.Value)
                .Select((KeyValuePair<string, DateTimeOffset> pair) => pair.Key)
                .Order()
                .ToList();

            string target = name ?? "all";

            if (!result.Succeeded && renewed.Count == 0)
            {
                await audit.WriteAsync(
                    actor,
                    "certbot.renew",
                    target,
                    false,
                    Truncate(result.CombinedOutput),
                    cancellationToken
                );

                return new CertificateOperation(
                    false,
                    "certbot could not renew the certificates. The output below says why.",
                    result.CombinedOutput
                );
            }

            if (renewed.Count == 0)
            {
                return new CertificateOperation(
                    true,
                    "Nothing to renew yet: no certificate is close enough to its expiry.",
                    result.CombinedOutput
                );
            }

            // nginx keeps serving the old certificate until it is reloaded.
            ConfigResult reload = await store.ReloadNginxAsync(actor, cancellationToken);

            await audit.WriteAsync(
                actor,
                "certbot.renew",
                string.Join(", ", renewed),
                result.Succeeded && reload.Succeeded,
                Truncate(result.CombinedOutput),
                cancellationToken
            );

            string summary = $"Renewed {string.Join(", ", renewed)}.";

            if (!reload.Succeeded)
            {
                return new CertificateOperation(
                    false,
                    summary + " nginx could not be reloaded, so it still serves the old certificate: " + reload.Message,
                    result.CombinedOutput
                );
            }

            return new CertificateOperation(
                result.Succeeded,
                summary + " nginx was reloaded.",
                result.CombinedOutput,
                NginxReloaded: true
            );
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<CertificateOperation> DeleteAsync(
        string name,
        string actor,
        CancellationToken cancellationToken = default
    )
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);

        if (settings.CertbotMode == CertbotMode.Disabled)
        {
            return new CertificateOperation(false, "Certificate management is disabled in the settings.");
        }

        CertificateListing listing = await catalog.ListAsync(cancellationToken);
        CertificateEntry? entry = listing.Certificates
            .FirstOrDefault((CertificateEntry item) => item.Certificate.Name == name);

        if (entry is null)
        {
            return new CertificateOperation(false, $"There is no certificate named {name}.");
        }

        if (entry.Usage.Count > 0)
        {
            string users = string.Join(", ", entry.Usage.Select((CertificateUsage usage) => usage.FileName).Distinct());

            return new CertificateOperation(
                false,
                $"{name} is still used by {users}. Change those configs first."
            );
        }

        await Gate.WaitAsync(cancellationToken);

        try
        {
            HostCommandResult result = await RunCertbotAsync(
                settings,
                ["delete", "--cert-name", name, "--non-interactive"],
                cancellationToken
            );

            await audit.WriteAsync(
                actor,
                "certbot.delete",
                name,
                result.Succeeded,
                Truncate(result.CombinedOutput),
                cancellationToken
            );

            return result.Succeeded
                ? new CertificateOperation(
                    true,
                    $"The certificate {name} was deleted.",
                    result.CombinedOutput
                )
                : new CertificateOperation(
                    false,
                    "certbot could not delete the certificate.",
                    result.CombinedOutput
                );
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static string? ValidateIssue(IssueRequest request, AppSettings settings)
    {
        if (settings.CertbotMode == CertbotMode.Disabled)
        {
            return "Certificate management is disabled in the settings.";
        }

        if (request.Domains.Count == 0)
        {
            return "Enter at least one domain name.";
        }

        if (request.Domains.Count > MaximumDomains)
        {
            return $"At most {MaximumDomains} domain names are allowed.";
        }

        foreach (string domain in request.Domains)
        {
            if (domain.Contains('*', StringComparison.Ordinal))
            {
                return
                    "Wildcard certificates need a DNS challenge, which Roastgate does not drive. Use a certificate per host name.";
            }

            if (domain.Length > 253 || !DomainPattern().IsMatch(domain))
            {
                return $"\"{domain}\" is not a valid public domain name.";
            }
        }

        if (
            request.Domains.Select((string domain) => domain.ToLowerInvariant()).Distinct().Count() !=
            request.Domains.Count
        )
        {
            return "A domain name is listed twice.";
        }

        if (
            !string.IsNullOrWhiteSpace(request.CertName)
            && !CertificateNamePattern().IsMatch(request.CertName.Trim())
        )
        {
            return "The certificate name may only contain lowercase letters, digits, dots, hyphens and underscores.";
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && !EmailPattern().IsMatch(request.Email.Trim()))
        {
            return "The e-mail address is not valid.";
        }

        if (!Directory.Exists(settings.AcmeWebroot))
        {
            return
                $"The ACME webroot {settings.AcmeWebroot} does not exist or is not visible to Roastgate. Let's Encrypt validates your domains by reading files from it.";
        }

        return null;
    }

    private Task<HostCommandResult> RunCertbotAsync(
        AppSettings settings,
        List<string> arguments,
        CancellationToken cancellationToken
    )
    {
        if (settings.CertbotMode == CertbotMode.Host)
        {
            return hostExec.RunAsync("certbot", arguments, CertbotTimeout, cancellationToken);
        }

        // The embedded certbot keeps its certificates where nginx reads them and its scratch files in the data volume.
        arguments.AddRange(
        [
            "--config-dir", settings.LetsEncryptDirectory,
            "--work-dir", Path.Combine(options.DataDirectory, "certbot", "work"),
            "--logs-dir", Path.Combine(options.DataDirectory, "certbot", "logs"),
        ]);

        return localExec.RunAsync("certbot", arguments, CertbotTimeout, cancellationToken);
    }

    private async Task<Dictionary<string, DateTimeOffset>> SnapshotAsync(CancellationToken cancellationToken)
    {
        CertificateListing listing = await catalog.ListAsync(cancellationToken);

        return listing.Certificates.ToDictionary(
            (CertificateEntry entry) => entry.Certificate.Name,
            (CertificateEntry entry) => entry.Certificate.NotAfter
        );
    }

    private static string? Truncate(string? text)
    {
        return text is not null && text.Length > MaximumAuditDetailLength
            ? text[..MaximumAuditDetailLength] + "..."
            : text;
    }
}
