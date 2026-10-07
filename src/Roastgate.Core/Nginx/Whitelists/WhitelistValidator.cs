using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Roastgate.Core.Nginx.Whitelists;

/// <summary>
/// Strict address validation. <see cref="IPAddress.TryParse(string, out IPAddress)"/> alone is far too lenient
/// (it accepts "1", "1.2" or "0x7f.1" as IPv4 addresses), and nginx would reject or misread those.
/// </summary>
public static partial class WhitelistValidator
{
    [GeneratedRegex(@"^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}\z")]
    private static partial Regex StrictIpv4();

    [GeneratedRegex(@"^\d{1,3}\z")]
    private static partial Regex PrefixLength();

    /// <summary>Parses "1.2.3.4", "10.0.0.0/8", "2001:db8::/32"... Returns false for anything nginx would not read as an address.</summary>
    public static bool TryParse(string? text, out IPAddress? address, out int prefix, out bool hasPrefix)
    {
        address = null;
        prefix = 0;
        hasPrefix = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Trim().Split('/');
        if (parts.Length > 2)
        {
            return false;
        }

        string host = parts[0];
        bool isIpv6 = host.Contains(':', StringComparison.Ordinal);
        if (isIpv6)
        {
            if (host.Contains('%', StringComparison.Ordinal) || !IPAddress.TryParse(host, out IPAddress? parsedV6) || parsedV6.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }

            address = parsedV6;
        }
        else
        {
            if (!StrictIpv4().IsMatch(host) || !IPAddress.TryParse(host, out IPAddress? parsedV4))
            {
                return false;
            }

            address = parsedV4;
        }

        if (parts.Length == 2)
        {
            int maximum = isIpv6 ? 128 : 32;
            if (!PrefixLength().IsMatch(parts[1]) || !int.TryParse(parts[1], out prefix) || prefix > maximum)
            {
                address = null;
                return false;
            }

            hasPrefix = true;
        }
        else
        {
            prefix = isIpv6 ? 128 : 32;
        }

        return true;
    }

    public static bool IsValid(string? text)
    {
        return TryParse(text, out _, out _, out _);
    }

    /// <summary>Errors block saving; warnings are shown but do not.</summary>
    public static IReadOnlyList<WhitelistProblem> Validate(IReadOnlyList<WhitelistEntry> entries)
    {
        List<WhitelistProblem> problems = [];
        Dictionary<string, int> seen = new(StringComparer.Ordinal);

        for (int index = 0; index < entries.Count; index++)
        {
            string address = entries[index].Address;
            if (!TryParse(address, out IPAddress? parsed, out int prefix, out _))
            {
                problems.Add(new WhitelistProblem(
                    ProblemSeverity.Error,
                    index,
                    string.IsNullOrWhiteSpace(address)
                        ? "The address is empty."
                        : $"\"{address.Trim()}\" is not a valid IPv4 or IPv6 address, or CIDR range."));
                continue;
            }

            if (HasHostBits(parsed!, prefix))
            {
                problems.Add(new WhitelistProblem(
                    ProblemSeverity.Warning,
                    index,
                    $"{address.Trim()} has bits set to the right of the /{prefix} prefix. nginx ignores them (it warns in its log); did you mean {Network(parsed!, prefix)}/{prefix}?"));
            }

            string key = Network(parsed!, prefix) + "/" + prefix;
            if (seen.TryGetValue(key, out int first))
            {
                problems.Add(new WhitelistProblem(ProblemSeverity.Warning, index, $"{address.Trim()} is already listed (line {first + 1})."));
            }
            else
            {
                seen[key] = index;
            }

            string? comment = entries[index].Comment;
            if (comment is not null && (comment.Contains('\n', StringComparison.Ordinal) || comment.Contains('\r', StringComparison.Ordinal)))
            {
                problems.Add(new WhitelistProblem(ProblemSeverity.Error, index, "A comment must fit on one line."));
            }
        }

        return problems;
    }

    private static bool HasHostBits(IPAddress address, int prefix)
    {
        byte[] bytes = address.GetAddressBytes();
        for (int bit = prefix; bit < bytes.Length * 8; bit++)
        {
            if ((bytes[bit / 8] & (0x80 >> (bit % 8))) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IPAddress Network(IPAddress address, int prefix)
    {
        byte[] bytes = address.GetAddressBytes();
        for (int bit = prefix; bit < bytes.Length * 8; bit++)
        {
            bytes[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        }

        return new IPAddress(bytes);
    }
}
