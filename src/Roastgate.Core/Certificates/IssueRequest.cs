namespace Roastgate.Core.Certificates;

/// <param name="Domains">
/// The names the certificate must cover. Wildcards need DNS validation and are not supported.
/// </param>
/// <param name="CertName">The directory name under live/. Defaults to the first domain.</param>
/// <param name="Staging">
/// Use Let's Encrypt's staging server: certificates are not trusted, but rate limits are generous.
/// </param>
/// <param name="DryRun">Run the whole validation against the staging server without keeping any certificate.</param>
public sealed record IssueRequest(
    IReadOnlyList<string> Domains,
    string? CertName,
    string? Email,
    bool Staging,
    bool DryRun
);
