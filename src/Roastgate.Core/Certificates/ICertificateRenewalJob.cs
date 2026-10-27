namespace Roastgate.Core.Certificates;

public interface ICertificateRenewalJob
{
    /// <summary>One renewal pass. Does nothing unless certificates are managed by the embedded certbot.</summary>
    Task RunOnceAsync(CancellationToken cancellationToken = default);

    Task<RenewalStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
