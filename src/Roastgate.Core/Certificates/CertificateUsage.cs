using Roastgate.Core.Nginx;

namespace Roastgate.Core.Certificates;

/// <summary>A config that points <c>ssl_certificate</c> at the certificate.</summary>
public sealed record CertificateUsage(ConfigArea Area, string FileName, bool FileEnabled, int Line);
