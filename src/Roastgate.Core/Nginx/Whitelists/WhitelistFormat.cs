using System.Text;
using System.Text.RegularExpressions;

namespace Roastgate.Core.Nginx.Whitelists;

/// <summary>
/// Reads and writes whitelist files. The structured form is deliberately narrow (a leading comment block, then
/// <c>allow</c> lines with an optional trailing comment): anything else is reported as unsupported so that the
/// structured editor never rewrites, and so never destroys, content it does not understand.
/// </summary>
public static partial class WhitelistFormat
{
    [GeneratedRegex(@"^\s*allow\s+(?<address>[^\s;#]+)\s*;\s*(?:#\s?(?<comment>.*?))?\s*\z")]
    private static partial Regex AllowLine();

    public static WhitelistParseResult Parse(string content)
    {
        List<string> header = [];
        List<WhitelistEntry> entries = [];
        List<string> unsupported = [];

        string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string trimmed = line.Trim();
            int number = index + 1;
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                if (entries.Count == 0 && unsupported.Count == 0)
                {
                    header.Add(trimmed.TrimStart('#').TrimStart().TrimEnd());
                }
                else
                {
                    unsupported.Add($"line {number}: a comment between entries (only a leading comment block can be kept)");
                }

                continue;
            }

            Match match = AllowLine().Match(line);
            if (!match.Success)
            {
                unsupported.Add($"line {number}: \"{Shorten(trimmed)}\" is not a plain \"allow\" line");
                continue;
            }

            string address = match.Groups["address"].Value;
            if (address.Equals("all", StringComparison.OrdinalIgnoreCase) || address.StartsWith("unix:", StringComparison.OrdinalIgnoreCase))
            {
                unsupported.Add($"line {number}: \"allow {address}\" is not an IP address");
                continue;
            }

            string comment = match.Groups["comment"].Value.Trim();
            entries.Add(new WhitelistEntry(address, comment.Length == 0 ? null : comment));
        }

        if (unsupported.Count > 0)
        {
            return new WhitelistParseResult(null, unsupported);
        }

        string? headerText = header.Count == 0 ? null : string.Join('\n', header);
        return new WhitelistParseResult(new WhitelistDocument(headerText, entries), []);
    }

    public static string Write(WhitelistDocument document)
    {
        StringBuilder builder = new();
        if (!string.IsNullOrWhiteSpace(document.Header))
        {
            foreach (string line in document.Header.Replace("\r\n", "\n").Split('\n'))
            {
                builder.Append("# ").Append(line.Trim()).Append('\n');
            }

            if (document.Entries.Count > 0)
            {
                builder.Append('\n');
            }
        }

        foreach (WhitelistEntry entry in document.Entries)
        {
            builder.Append("allow ").Append(entry.Address.Trim()).Append(';');
            string? comment = entry.Comment?.Replace("\r", " ").Replace("\n", " ").Trim().TrimStart('#').Trim();
            if (!string.IsNullOrEmpty(comment))
            {
                builder.Append(" # ").Append(comment);
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string Shorten(string text)
    {
        return text.Length <= 60 ? text : text[..57] + "...";
    }
}
