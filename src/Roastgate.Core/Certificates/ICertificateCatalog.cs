namespace Roastgate.Core.Certificates;

public interface ICertificateCatalog
{
    Task<CertificateListing> ListAsync(CancellationToken cancellationToken = default);
}
