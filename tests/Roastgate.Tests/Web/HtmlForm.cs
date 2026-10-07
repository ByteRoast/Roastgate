using System.Net;
using System.Text.RegularExpressions;

namespace Roastgate.Tests.Web;

/// <summary>Just enough HTML parsing to drive Blazor static server rendered forms from a test.</summary>
internal static partial class HtmlForm
{
    [GeneratedRegex(@"<input\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputTag();

    [GeneratedRegex(@"\bname=""(?<value>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex NameAttribute();

    [GeneratedRegex(@"\bvalue=""(?<value>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ValueAttribute();

    /// <summary>The hidden fields (antiforgery token, form handler name) a browser would post back.</summary>
    public static Dictionary<string, string> HiddenFields(string html)
    {
        Dictionary<string, string> fields = [];
        foreach (Match input in InputTag().Matches(html))
        {
            if (!input.Value.Contains("type=\"hidden\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Match name = NameAttribute().Match(input.Value);
            Match value = ValueAttribute().Match(input.Value);
            if (name.Success)
            {
                fields[WebUtility.HtmlDecode(name.Groups["value"].Value)] = value.Success ? WebUtility.HtmlDecode(value.Groups["value"].Value) : string.Empty;
            }
        }

        return fields;
    }

    public static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string username, string password, string path = "/login")
    {
        string page = await client.GetStringAsync(path);
        Dictionary<string, string> fields = HiddenFields(page);
        fields["Input.Username"] = username;
        fields["Input.Password"] = password;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }
}
