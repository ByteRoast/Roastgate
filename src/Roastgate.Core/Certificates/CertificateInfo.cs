namespace Roastgate.Core.Certificates;

/// <summary>A certificate found in Let's Encrypt's <c>live</c> directory.</summary>
public sealed record CertificateInfo(
    string Name,
    IReadOnlyList<string> Domains,
    string Issuer,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool IsStaging,
    string FullChainPath,
    string PrivateKeyPath
)
{
    public int DaysLeft(DateTimeOffset now)
    {
        return (int)Math.Floor((NotAfter - now).TotalDays);
    }

    /// <summary>
    /// True when the certificate covers the host name (an exact name, or a wildcard such as *.example.com).
    /// </summary>
    public bool Covers(string hostName)
    {
        foreach (string domain in Domains)
        {
            if (string.Equals(domain, hostName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (
                domain.StartsWith("*.", StringComparison.Ordinal)
                && hostName.EndsWith(domain[1..], StringComparison.OrdinalIgnoreCase)
                && hostName.IndexOf('.') == hostName.Length - domain.Length + 1
            )
            {
                return true;
            }
        }

        return false;
    }
}
