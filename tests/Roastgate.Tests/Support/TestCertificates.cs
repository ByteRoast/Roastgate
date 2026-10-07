using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Roastgate.Tests.Support;

/// <summary>Real X.509 certificates, generated on the fly, laid out like certbot's live directory.</summary>
internal static class TestCertificates
{
    public static string CreatePem(string commonName, string[] dnsNames, DateTimeOffset notBefore, DateTimeOffset notAfter, string issuer = "Test Issuer")
    {
        using ECDsa authorityKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest authorityRequest = new($"CN={issuer}", authorityKey, HashAlgorithmName.SHA256);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using X509Certificate2 authority = authorityRequest.CreateSelfSigned(notBefore.AddDays(-1), notAfter.AddDays(1));

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new($"CN={commonName}", key, HashAlgorithmName.SHA256);
        if (dnsNames.Length > 0)
        {
            SubjectAlternativeNameBuilder names = new();
            foreach (string name in dnsNames)
            {
                names.AddDnsName(name);
            }

            request.CertificateExtensions.Add(names.Build());
        }

        byte[] serial = RandomNumberGenerator.GetBytes(8);
        using X509Certificate2 leaf = request.Create(authority, notBefore, notAfter, serial);
        // Like a real fullchain.pem: one block per certificate, each ending with a line break.
        return leaf.ExportCertificatePem() + "\n" + authority.ExportCertificatePem() + "\n";
    }

    /// <summary>Writes live/&lt;name&gt;/fullchain.pem and privkey.pem like certbot does, and returns the fullchain path.</summary>
    public static string WriteLive(string letsEncryptDirectory, string name, string[] dnsNames, DateTimeOffset notAfter, string issuer = "Test Issuer")
    {
        string directory = Path.Combine(letsEncryptDirectory, "live", name);
        Directory.CreateDirectory(directory);
        string fullChain = Path.Combine(directory, "fullchain.pem");
        File.WriteAllText(fullChain, CreatePem(dnsNames.Length > 0 ? dnsNames[0] : name, dnsNames, notAfter.AddDays(-90), notAfter, issuer));
        File.WriteAllText(Path.Combine(directory, "privkey.pem"), "-----BEGIN PRIVATE KEY-----\nplaceholder\n-----END PRIVATE KEY-----\n");
        return fullChain;
    }
}
