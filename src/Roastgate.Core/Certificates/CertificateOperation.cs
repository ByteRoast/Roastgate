namespace Roastgate.Core.Certificates;

/// <param name="Output">What certbot printed (shown to the user, since it explains failures well).</param>
public sealed record CertificateOperation(
    bool Succeeded,
    string Message,
    string? Output = null,
    bool NginxReloaded = false
);
