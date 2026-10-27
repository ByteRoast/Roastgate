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
