using System.Text;
using Roastgate.Core.Extensions;

namespace Roastgate.Core.Nginx.Generation;

/// <param name="Locations">Where the whitelists live, to build the include lines.</param>
/// <param name="AcmeWebroot">The Let's Encrypt challenge directory from the settings.</param>
/// <param name="FileName">The config file name, used to give the websocket map variable a name that is unique across files.</param>
/// <param name="NginxVersion">The version running on the host, when known. Directives that changed between versions depend on it.</param>
public sealed record GenerationContext(
    ConfigLocations Locations,
    string AcmeWebroot,
    string FileName,
    Version? NginxVersion = null)
{
    /// <summary>
    /// nginx 1.25.1 replaced the <c>http2</c> parameter of <c>listen</c> by a separate <c>http2 on;</c> directive, which older versions
    /// reject. The parameter form is accepted everywhere (newer versions only warn), so it is also used when the version is unknown.
    /// </summary>
    public bool UsesHttp2Directive => NginxVersion is { } version && version >= new Version(1, 25, 1);
}

/// <summary>Turns a validated <see cref="SiteSpec"/> into nginx configuration text. It does no validation of its own.</summary>
public static class SiteGenerator
{
    public static string Generate(SiteSpec spec, GenerationContext context)
    {
        NginxWriter writer = new();

        string upgradeVariable = "$rg_upgrade_" + VariableSuffix(context.FileName);

        if (spec.Kind == SiteKind.ReverseProxy && spec.Websockets)
        {
            writer.Open($"map $http_upgrade {upgradeVariable}");
            writer.Line("default upgrade;");
            writer.Line("''      close;");
            writer.Close();
            writer.Blank();
        }

        bool redirectToHttps = spec.EnableTls && spec.ForceHttps;

        WriteServer(writer, spec, context, tls: false, redirect: redirectToHttps, upgradeVariable);

        if (spec.EnableTls)
        {
            writer.Blank();
            WriteServer(writer, spec, context, tls: true, redirect: false, upgradeVariable);
        }

        return writer.ToString();
    }

    private static void WriteServer(NginxWriter writer, SiteSpec spec, GenerationContext context, bool tls,
        bool redirect, string upgradeVariable)
    {
        int port = tls ? spec.HttpsPort : spec.HttpPort;

        writer.Open("server");

        string parameters = tls ? (spec.Http2 && !context.UsesHttp2Directive ? " ssl http2" : " ssl") : string.Empty;

        writer.Line($"listen {port}{parameters};");

        if (spec.Ipv6)
        {
            writer.Line($"listen [::]:{port}{parameters};");
        }

        if (tls && spec.Http2 && context.UsesHttp2Directive)
        {
            writer.Line("http2 on;");
        }

        writer.Line($"server_name {string.Join(' ', spec.ServerNames)};");

        if (tls)
        {
            writer.Blank();
            writer.Line($"ssl_certificate {spec.CertificatePath};");
            writer.Line($"ssl_certificate_key {spec.CertificateKeyPath};");
            writer.Line("ssl_protocols TLSv1.2 TLSv1.3;");
        }

        WriteAccess(writer, spec, context);

        if (!redirect)
        {
            WriteLimitsAndHeaders(writer, spec, tls);
        }

        if (spec.ServeAcmeChallenge)
        {
            writer.Blank();
            writer.Open("location ^~ /.well-known/acme-challenge/");

            if (spec.Whitelists.Count > 0)
            {
                writer.Line("allow all;");
            }

            writer.Line($"root {context.AcmeWebroot};");
            writer.Close();
        }

        if (redirect)
        {
            string authority = spec.HttpsPort == 443 ? "$host" : $"$host:{spec.HttpsPort}";

            writer.Blank();
            writer.Open("location /");
            writer.Line($"return 301 https://{authority}$request_uri;");
            writer.Close();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(spec.ExtraServerDirectives))
            {
                writer.Blank();
                writer.Lines(spec.ExtraServerDirectives);
            }

            writer.Blank();

            if (spec.Kind == SiteKind.ReverseProxy)
            {
                WriteProxyLocation(writer, spec, upgradeVariable);
            }
            else
            {
                WriteStaticLocations(writer, spec);
            }
        }

        writer.Close();
    }

    private static void WriteAccess(NginxWriter writer, SiteSpec spec, GenerationContext context)
    {
        if (spec.Whitelists.Count == 0)
        {
            return;
        }

        writer.Blank();

        foreach (string whitelist in spec.Whitelists)
        {
            writer.Line($"include {context.Locations.WhitelistIncludePath(whitelist)};");
        }

        writer.Line("deny all;");

        string? deniedReturn = spec.DeniedBehavior switch
        {
            DeniedBehavior.Redirect => $"return 302 {spec.DeniedUrl};",
            DeniedBehavior.NotFound => "return 404;",
            DeniedBehavior.CloseConnection => "return 444;",
            // The case Forbidden is already handled and do not require another block
            _ => null,
        };

        if (deniedReturn is null)
        {
            return;
        }

        writer.Blank();
        writer.Line("error_page 403 = @rg_denied;");
        writer.Blank();
        writer.Open("location @rg_denied");
        writer.Line("allow all;");
        writer.Line(deniedReturn);
        writer.Close();
    }

    private static void WriteLimitsAndHeaders(NginxWriter writer, SiteSpec spec, bool tls)
    {
        bool hasLimit = !string.IsNullOrWhiteSpace(spec.MaxBodySize);

        if (hasLimit || spec.SecurityHeaders || (tls && spec.Hsts) || spec.BlockCommonExploits)
        {
            writer.Blank();
        }

        if (hasLimit)
        {
            writer.Line($"client_max_body_size {spec.MaxBodySize};");
        }

        if (spec.SecurityHeaders)
        {
            writer.Line("add_header X-Content-Type-Options nosniff always;");
            writer.Line("add_header X-Frame-Options SAMEORIGIN always;");
            writer.Line("add_header Referrer-Policy strict-origin-when-cross-origin always;");
        }

        if (tls && spec.Hsts)
        {
            writer.Line("add_header Strict-Transport-Security \"max-age=31536000\" always;");
        }

        if (spec.BlockCommonExploits)
        {
            writer.Line("if ($request_method ~* \"^(TRACE|TRACK)$\") { return 405; }");
            writer.Open("location ~ /\\.(?!well-known)");
            writer.Line("deny all;");
            writer.Close();
        }
    }

    private static void WriteProxyLocation(NginxWriter writer, SiteSpec spec, string upgradeVariable)
    {
        writer.Open("location /");
        writer.Line($"proxy_pass {spec.Upstream};");
        writer.Line("proxy_http_version 1.1;");

        if (spec.Upstream.StartsWith("https://", StringComparison.Ordinal))
        {
            writer.Line("proxy_ssl_server_name on;");
        }

        writer.Line("proxy_set_header Host $host;");
        writer.Line("proxy_set_header X-Real-IP $remote_addr;");
        writer.Line("proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;");
        writer.Line("proxy_set_header X-Forwarded-Proto $scheme;");
        writer.Line("proxy_set_header X-Forwarded-Host $host;");

        if (spec.Websockets)
        {
            writer.Line("proxy_set_header Upgrade $http_upgrade;");
            writer.Line($"proxy_set_header Connection {upgradeVariable};");
        }

        if (spec.ProxyTimeoutSeconds is { } timeout)
        {
            writer.Line($"proxy_read_timeout {timeout}s;");
            writer.Line($"proxy_send_timeout {timeout}s;");
        }

        if (!string.IsNullOrWhiteSpace(spec.ExtraLocationDirectives))
        {
            writer.Lines(spec.ExtraLocationDirectives);
        }

        writer.Close();
    }

    private static void WriteStaticLocations(NginxWriter writer, SiteSpec spec)
    {
        writer.Line($"root {spec.RootDirectory};");
        writer.Line("index index.html index.htm;");
        writer.Blank();

        WriteDotDirectoryExceptions(writer, spec);

        writer.Open("location /");
        writer.Line(spec.SpaFallback ? "try_files $uri $uri/ /index.html;" : "try_files $uri $uri/ =404;");

        if (spec.FancyIndex is { } fancyIndex)
        {
            WriteFancyIndex(writer, fancyIndex);
        }

        if (!string.IsNullOrWhiteSpace(spec.ExtraLocationDirectives))
        {
            writer.Lines(spec.ExtraLocationDirectives);
        }

        writer.Close();

        if (spec.CacheStaticAssets)
        {
            // "expires" rather than add_header: add_header in a location would drop the security headers of the server block.
            writer.Blank();
            writer.Open("location ~* \\.(?:css|js|mjs|jpg|jpeg|gif|png|svg|ico|webp|avif|woff2?)$");
            writer.Line("expires 30d;");
            writer.Line("try_files $uri =404;");
            writer.Close();
        }
    }

    private static void WriteDotDirectoryExceptions(NginxWriter writer, SiteSpec spec)
    {
        if (spec.FancyIndex is not { } options || !spec.BlockCommonExploits)
        {
            return;
        }

        string?[] paths = [options.Header, options.Footer, options.CssHref];

        IEnumerable<string> directories = paths
            .OfType<string>()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path[..path.LastIndexOf('/')])
            .Where(path => path.Split('/').Any(segment => segment.StartsWith('.')))
            .Distinct();

        foreach (string directory in directories)
        {
            writer.Open($"location ^~ {directory}/");
            writer.Line("try_files $uri =404;");
            writer.Close();
            writer.Blank();
        }
    }

    /// <summary>A variable name suffix that is unique per config file (nginx refuses two maps that define the same variable).</summary>
    internal static string VariableSuffix(string fileName)
    {
        string name = fileName.EndsWith(ConfigNames.ConfigSuffix, StringComparison.Ordinal)
            ? fileName[..^ConfigNames.ConfigSuffix.Length]
            : fileName;

        StringBuilder builder = new();

        foreach (char character in name.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private static void WriteFancyIndex(NginxWriter writer, FancyIndexOptions options)
    {
        writer.Line("fancyindex on;");
        writer.Line($"fancyindex_exact_size {BooleanToOnOff(options.ExactSize)};");
        writer.Line($"fancyindex_localtime {BooleanToOnOff(options.LocalTime)};");
        writer.Line($"fancyindex_show_path {BooleanToOnOff(options.ShowPath)};");
        writer.Line($"fancyindex_directories_first {BooleanToOnOff(options.DirectoriesFirst)};");
        writer.Line($"fancyindex_case_sensitive {BooleanToOnOff(options.CaseSensitive)};");
        writer.Line($"fancyindex_show_dotfiles {BooleanToOnOff(options.ShowDotFiles)};");
        writer.Line($"fancyindex_hide_symlinks {BooleanToOnOff(options.HideSymlinks)};");
        writer.Line($"fancyindex_hide_parent_dir {BooleanToOnOff(options.HideParentDir)};");
        writer.Line($"fancyindex_default_sort {options.DefaultSort.GetDescription()};");
        writer.Line($"fancyindex_time_format \"{options.TimeFormat}\";");

        if (!string.IsNullOrWhiteSpace(options.Header))
        {
            writer.Line($"fancyindex_header \"{options.Header}\";");
        }

        if (!string.IsNullOrWhiteSpace(options.Footer))
        {
            writer.Line($"fancyindex_footer \"{options.Footer}\";");
        }

        if (!string.IsNullOrWhiteSpace(options.CssHref))
        {
            writer.Line($"fancyindex_css_href \"{options.CssHref}\";");
        }

        if (options.Ignore.Count > 0)
        {
            writer.Line($"fancyindex_ignore {string.Join(' ', options.Ignore)};");
        }
    }

    private static string BooleanToOnOff(bool value)
    {
        return value ? "on" : "off";
    }

    private sealed class NginxWriter
    {
        private readonly StringBuilder _builder = new();
        private int _depth;

        public void Blank()
        {
            // Never two blank lines in a row, and never one straight after an opening brace.
            string text = _builder.ToString();
            if (text.Length == 0 || text.EndsWith("\n\n", StringComparison.Ordinal) ||
                text.EndsWith("{\n", StringComparison.Ordinal))
            {
                return;
            }

            _builder.Append('\n');
        }

        public void Line(string text)
        {
            _builder.Append(' ', _depth * 4).Append(text).Append('\n');
        }

        /// <summary>Free text from the user: each line is indented to the current depth, its own relative indentation is kept.</summary>
        public void Lines(string text)
        {
            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string trimmed = line.TrimEnd();

                if (trimmed.Length > 0)
                {
                    _builder.Append(' ', _depth * 4).Append(trimmed.Replace("\t", "    ")).Append('\n');
                }
            }
        }

        public void Open(string header)
        {
            Line(header + " {");

            _depth++;
        }

        public void Close()
        {
            _depth--;

            Line("}");
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
