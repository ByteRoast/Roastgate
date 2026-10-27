namespace Roastgate.Core.Certificates;

public sealed record CertificateEntry(CertificateInfo Certificate, IReadOnlyList<CertificateUsage> Usage);
