using Roastgate.Core.Hosting;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Setup;

public enum DiagnosticStatus
{
    Ok,
    Warning,
    Error,
}

public sealed record DiagnosticCheck(string Name, DiagnosticStatus Status, string Message);

/// <summary>Checks that the host is ready to be driven: command access, nginx, directories and certbot.</summary>
public sealed class HostDiagnostics(IHostExec hostExec, NginxProbe nginxProbe, RoastgateOptions options)
{
    private static readonly string[] RequiredNginxDirectories = ["conf.d", "sites-available", "sites-enabled"];

    public async Task<IReadOnlyList<DiagnosticCheck>> RunAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        List<DiagnosticCheck> checks = [];

        checks.Add(await CheckHostAccessAsync(cancellationToken));

        NginxProbeResult probe = await nginxProbe.ProbeAsync(cancellationToken);
        checks.Add(probe.Available
            ? new DiagnosticCheck("nginx", DiagnosticStatus.Ok, $"nginx {probe.Version ?? "(unknown version)"} found on the host.")
            : new DiagnosticCheck("nginx", DiagnosticStatus.Error, "nginx could not be run on the host. " + probe.Output));

        if (probe.Available)
        {
            HostCommandResult test = await hostExec.RunAsync("nginx", ["-t"], TimeSpan.FromSeconds(15), cancellationToken);
            checks.Add(test.Succeeded
                ? new DiagnosticCheck("nginx configuration", DiagnosticStatus.Ok, "The current configuration passes nginx -t.")
                : new DiagnosticCheck("nginx configuration", DiagnosticStatus.Error, "nginx -t fails on the current configuration. " + test.CombinedOutput));
        }

        foreach (string directory in RequiredNginxDirectories)
        {
            string path = Path.Combine(settings.NginxRoot, directory);
            checks.Add(Directory.Exists(path)
                ? new DiagnosticCheck(directory, DiagnosticStatus.Ok, $"{path} is visible to Roastgate.")
                : new DiagnosticCheck(directory, DiagnosticStatus.Error, $"{path} does not exist. Mount the nginx directory into the Roastgate container."));
        }

        checks.Add(Directory.Exists(settings.WhitelistsDirectory)
            ? new DiagnosticCheck("whitelists", DiagnosticStatus.Ok, $"{settings.WhitelistsDirectory} is visible to Roastgate.")
            : new DiagnosticCheck("whitelists", DiagnosticStatus.Warning, $"{settings.WhitelistsDirectory} does not exist yet. It will be created when the first whitelist is saved."));

        checks.Add(Directory.Exists(settings.AcmeWebroot)
            ? new DiagnosticCheck("ACME webroot", DiagnosticStatus.Ok, $"{settings.AcmeWebroot} is visible to Roastgate.")
            : new DiagnosticCheck("ACME webroot", DiagnosticStatus.Warning, $"{settings.AcmeWebroot} does not exist. Certificates cannot be issued until it exists and nginx serves it under /.well-known/acme-challenge/."));

        if (settings.CertbotMode != CertbotMode.Disabled)
        {
            checks.Add(Directory.Exists(settings.LetsEncryptDirectory)
                ? new DiagnosticCheck("Let's Encrypt directory", DiagnosticStatus.Ok, $"{settings.LetsEncryptDirectory} is visible to Roastgate.")
                : new DiagnosticCheck(
                    "Let's Encrypt directory",
                    DiagnosticStatus.Warning,
                    $"{settings.LetsEncryptDirectory} does not exist yet. Mount the host's /etc/letsencrypt there so that nginx and Roastgate share the certificates."));
        }

        checks.Add(await CheckCertbotAsync(settings, cancellationToken));
        return checks;
    }

    private async Task<DiagnosticCheck> CheckHostAccessAsync(CancellationToken cancellationToken)
    {
        if (options.HostExecMode == HostExecMode.Direct)
        {
            return new DiagnosticCheck(
                "host access",
                DiagnosticStatus.Warning,
                "Direct mode: commands run inside Roastgate's own environment. This is meant for development only.");
        }

        HostCommandResult result = await hostExec.RunAsync("true", [], TimeSpan.FromSeconds(10), cancellationToken);
        return result.Succeeded
            ? new DiagnosticCheck("host access", DiagnosticStatus.Ok, "Commands run in the host namespaces (nsenter).")
            : new DiagnosticCheck(
                "host access",
                DiagnosticStatus.Error,
                "nsenter failed. The container needs privileged: true and pid: host. " + result.CombinedOutput);
    }

    private async Task<DiagnosticCheck> CheckCertbotAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        HostCommandResult result = await hostExec.RunAsync("certbot", ["--version"], TimeSpan.FromSeconds(10), cancellationToken);
        bool onHost = result.Succeeded;

        return settings.CertbotMode switch
        {
            CertbotMode.Disabled => new DiagnosticCheck("certbot", DiagnosticStatus.Ok, "Certificate management is disabled."),
            CertbotMode.Host when onHost => new DiagnosticCheck("certbot", DiagnosticStatus.Ok, $"Host certbot found ({result.CombinedOutput})."),
            CertbotMode.Host => new DiagnosticCheck("certbot", DiagnosticStatus.Error, "Host mode is selected but certbot was not found on the host."),
            _ when onHost => new DiagnosticCheck(
                "certbot",
                DiagnosticStatus.Warning,
                $"Embedded mode is selected, but certbot is also installed on the host ({result.CombinedOutput}). Make sure only one of them renews certificates (disable the host certbot timer, or switch to host mode)."),
            _ => new DiagnosticCheck("certbot", DiagnosticStatus.Ok, "The embedded certbot will be used (no certbot on the host)."),
        };
    }
}
