using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Roastgate.Core.Nginx;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Certificates;

/// <summary>
/// Reads the certificates straight from the files certbot maintains (<c>live/&lt;name&gt;/fullchain.pem</c>)
/// instead of parsing the text certbot prints, which changes between versions. Works the same for the embedded
/// and the host certbot.
/// </summary>
public sealed partial class CertificateCatalog(
    AppSettingsService settingsService,
    IManagedConfigReader configs
) : ICertificateCatalog
{
    [GeneratedRegex(@"^\s*ssl_certificate\s+(?<path>""[^""]*""|'[^']*'|[^\s;]+)\s*;")]
    private static partial Regex CertificateDirective();

    public async Task<CertificateListing> ListAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);

        string live = Path.Combine(settings.LetsEncryptDirectory, "live");

        if (!Directory.Exists(live))
        {
            return new CertificateListing([], live, false, []);
        }

        List<CertificateInfo> found = [];
        List<string> unreadable = [];

        foreach (string directory in Directory.EnumerateDirectories(live))
        {
            string name = Path.GetFileName(directory);
            string fullChain = Path.Combine(directory, "fullchain.pem");

            if (!File.Exists(fullChain))
            {
                continue;
            }

            CertificateInfo? info = Parse(
                name,
                fullChain,
                Path.Combine(directory, "privkey.pem"),
                await File.ReadAllTextAsync(fullChain, cancellationToken)
            );

            if (info is null)
            {
                unreadable.Add(name);
            }
            else
            {
                found.Add(info);
            }
        }

        IReadOnlyList<ManagedConfig> managed = await configs.ReadAllAsync(cancellationToken);

        List<CertificateEntry> entries = found
            .OrderBy((CertificateInfo certificate) => certificate.NotAfter)
            .Select((CertificateInfo certificate) => new CertificateEntry(
                certificate,
                FindUsage(certificate, managed, settings)
            ))
            .ToList();

        return new CertificateListing(entries, live, true, unreadable.Order().ToList());
    }

    /// <summary>Reads the first certificate of a PEM file (the leaf, in a fullchain). Null when it cannot be read.</summary>
    public static CertificateInfo? Parse(string name, string fullChainPath, string privateKeyPath, string pem)
    {
        try
        {
            using X509Certificate2 certificate = X509Certificate2.CreateFromPem(pem);

            string issuer = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true);

            bool isStaging = issuer.Contains("STAGING", StringComparison.OrdinalIgnoreCase)
                             || issuer.Contains("Fake LE", StringComparison.OrdinalIgnoreCase)
                             || certificate.Issuer.Contains("(STAGING)", StringComparison.OrdinalIgnoreCase);

            return new CertificateInfo(
                name,
                DomainsOf(certificate),
                issuer,
                new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
                isStaging,
                fullChainPath,
                privateKeyPath
            );
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static List<string> DomainsOf(X509Certificate2 certificate)
    {
        List<string> domains = [];

        X509Extension? extension = certificate.Extensions["2.5.29.17"];

        if (extension is not null)
        {
            X509SubjectAlternativeNameExtension names = new(extension.RawData, extension.Critical);

            domains.AddRange(names.EnumerateDnsNames());
        }

        if (domains.Count == 0)
        {
            string commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

            if (!string.IsNullOrWhiteSpace(commonName))
            {
                domains.Add(commonName);
            }
        }

        return domains;
    }

    private static List<CertificateUsage> FindUsage(
        CertificateInfo certificate,
        IReadOnlyList<ManagedConfig> managed,
        AppSettings settings
    )
    {
        string directory = NginxPaths.Normalize(
            Path.GetDirectoryName(certificate.FullChainPath) ?? string.Empty,
            settings.NginxRoot
        ) + "/";

        List<CertificateUsage> usage = [];

        foreach (ManagedConfig config in managed)
        {
            for (int index = 0; index < config.Lines.Length; index++)
            {
                Match match = CertificateDirective().Match(config.Lines[index]);

                if (match.Success && NginxPaths
                        .Normalize(NginxPaths.Unquote(match.Groups["path"].Value), settings.NginxRoot)
                        .StartsWith(directory, StringComparison.Ordinal)
                   )
                {
                    usage.Add(new CertificateUsage(config.Area, config.FileName, config.Enabled, index + 1));
                }
            }
        }

        return usage.OrderBy((CertificateUsage item) => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy((CertificateUsage item) => item.Line).ToList();
    }
}
