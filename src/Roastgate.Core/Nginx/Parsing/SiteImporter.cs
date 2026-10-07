using System.Text.RegularExpressions;
using Roastgate.Core.Nginx.Generation;

namespace Roastgate.Core.Nginx.Parsing;

/// <param name="Spec">The settings read from the file, or null when it cannot be imported without changing what it does.</param>
/// <param name="Notes">Things the user should know about the import (the wizard adds some defaults, comments are dropped...).</param>
/// <param name="Blockers">Why the file cannot be imported. Empty when <paramref name="Spec"/> is set.</param>
public sealed record SiteImportResult(SiteSpec? Spec, IReadOnlyList<string> Notes, IReadOnlyList<string> Blockers)
{
    public bool Succeeded => Spec is not null;
}

/// <summary>
/// Reads a hand-written site (one server block, or an HTTP redirect plus an HTTPS server block) into the wizard's settings.
/// Whatever it does not recognise is kept as free directives, and a file that the wizard could not reproduce faithfully
/// (a top-level <c>map</c>, a listen address, a different <c>proxy_set_header Host</c>...) is refused with the reason.
/// </summary>
public static partial class SiteImporter
{
    private static readonly Dictionary<string, string> DefaultProxyHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Host"] = "$host",
        ["X-Real-IP"] = "$remote_addr",
        ["X-Forwarded-For"] = "$proxy_add_x_forwarded_for",
        ["X-Forwarded-Proto"] = "$scheme",
        ["X-Forwarded-Host"] = "$host",
    };

    private static readonly string[] SecurityHeaderCanonicals =
    [
        Canonical("add_header X-Content-Type-Options nosniff always;"),
        Canonical("add_header X-Frame-Options SAMEORIGIN always;"),
        Canonical("add_header Referrer-Policy strict-origin-when-cross-origin always;"),
    ];

    private static readonly string HstsCanonical = Canonical("add_header Strict-Transport-Security \"max-age=31536000\" always;");
    private static readonly string TraceCanonical = Canonical("if ($request_method ~* \"^(TRACE|TRACK)$\") { return 405; }");
    private static readonly string DotfilesCanonical = Canonical("location ~ /\\.(?!well-known) { deny all; }");
    private static readonly string CacheCanonical = Canonical("location ~* \\.(?:css|js|mjs|jpg|jpeg|gif|png|svg|ico|webp|avif|woff2?)$ { expires 30d; try_files $uri =404; }");

    [GeneratedRegex(@"^(?<number>\d{1,5})(?<unit>s|m)?\z")]
    private static partial Regex DurationPattern();

    public static SiteImportResult Import(string content, ConfigLocations locations, string acmeWebroot)
    {
        NginxParseResult parsed = NginxParser.Parse(content);
        if (!parsed.Succeeded)
        {
            return Refuse($"The file cannot be read as nginx configuration ({parsed.Error}).");
        }

        List<string> blockers = [];
        List<string> notes = [];
        List<NginxStatement> servers = [];
        foreach (NginxStatement statement in parsed.Statements)
        {
            if (statement.Name == "server" && statement.IsBlock && statement.Arguments.Count == 0)
            {
                servers.Add(statement);
            }
            else
            {
                blockers.Add($"Line {statement.Line}: \"{statement.Name}\" outside a server block would be lost (the wizard only writes server blocks and its own WebSocket map).");
            }
        }

        if (servers.Count is 0 or > 2)
        {
            blockers.Add(servers.Count == 0
                ? "There is no server block in this file."
                : "This file has more than two server blocks. The wizard handles one site: an HTTP server, plus an HTTPS server when TLS is on.");
        }

        if (blockers.Count > 0)
        {
            return new SiteImportResult(null, notes, blockers);
        }

        Context context = new(locations, acmeWebroot.TrimEnd('/'), blockers);
        List<ServerReading> readings = servers.Select((NginxStatement server) => ReadServer(server, context)).ToList();
        if (blockers.Count > 0)
        {
            return new SiteImportResult(null, notes, blockers);
        }

        ServerReading main;
        ServerReading? redirect = null;
        if (readings.Count == 1)
        {
            main = readings[0];
            if (main.Tls)
            {
                blockers.Add("This file only has an HTTPS server. The wizard always writes an HTTP server next to it (a redirect), so importing would add one.");
            }
        }
        else
        {
            ServerReading[] tls = readings.Where((ServerReading reading) => reading.Tls).ToArray();
            if (tls.Length != 1)
            {
                blockers.Add("With two server blocks, exactly one must be the HTTPS server and the other the plain HTTP one.");
                return new SiteImportResult(null, notes, blockers);
            }

            main = tls[0];
            redirect = readings.Single((ServerReading reading) => !reading.Tls);
            CheckRedirect(redirect, main, blockers);
        }

        if (blockers.Count > 0)
        {
            return new SiteImportResult(null, notes, blockers);
        }

        SiteSpec spec = BuildSpec(main, redirect, blockers, notes);
        if (blockers.Count > 0)
        {
            return new SiteImportResult(null, notes, blockers);
        }

        if (content.Replace("\r\n", "\n").Split('\n').Any((string line) => line.TrimStart().StartsWith('#')))
        {
            notes.Add("Comments are kept only inside the extra directives; the other comment lines are dropped.");
        }

        return new SiteImportResult(spec, notes, []);
    }

    private static SiteImportResult Refuse(string reason)
    {
        return new SiteImportResult(null, [], [reason]);
    }

    private static string Canonical(string snippet)
    {
        return NginxParser.Parse(snippet).Statements[0].Canonical;
    }

    private sealed record Context(ConfigLocations Locations, string AcmeWebroot, List<string> Blockers);

    /// <summary>What was understood in one server block, and what was not (<see cref="Rest"/>).</summary>
    private sealed class ServerReading
    {
        public bool Tls { get; set; }

        public int? Port { get; set; }

        public bool Ipv6 { get; set; }

        public bool Http2 { get; set; }

        public List<string> Names { get; } = [];

        public string Certificate { get; set; } = string.Empty;

        public string CertificateKey { get; set; } = string.Empty;

        public List<string> Whitelists { get; } = [];

        public string? MaxBodySize { get; set; }

        public bool SecurityHeaders { get; set; }

        public bool Hsts { get; set; }

        public bool BlockExploits { get; set; }

        public bool AcmeChallenge { get; set; }

        public bool CacheAssets { get; set; }

        public string? Root { get; set; }

        public NginxStatement? MainLocation { get; set; }

        public List<NginxStatement> Rest { get; } = [];
    }

    private static ServerReading ReadServer(NginxStatement server, Context context)
    {
        ServerReading reading = new();
        List<NginxStatement> remaining = server.Children!.ToList();
        ReadListens(remaining, reading, context);
        ReadNames(remaining, reading, context);
        ReadTls(remaining, reading, context);
        ReadAccess(remaining, reading, context);
        ReadLimitsAndHeaders(remaining, reading);
        ReadLocations(remaining, reading, context);
        reading.Rest.AddRange(remaining);
        return reading;
    }

    private static void ReadListens(List<NginxStatement> remaining, ServerReading reading, Context context)
    {
        bool anySsl = false;
        bool anyPlain = false;
        int? v6Port = null;
        foreach (NginxStatement listen in remaining.Where((NginxStatement statement) => statement.Name == "listen").ToList())
        {
            remaining.Remove(listen);
            if (listen.Arguments.Count == 0)
            {
                continue;
            }

            string address = listen.Arguments[0];
            bool v6 = address.StartsWith("[::]:", StringComparison.Ordinal);
            string portText = v6 ? address[5..] : address;
            if (!int.TryParse(portText, out int port) || port is < 1 or > 65535)
            {
                context.Blockers.Add($"Line {listen.Line}: \"listen {address}\" binds a specific address or socket, which the wizard does not write.");
                continue;
            }

            bool ssl = false;
            foreach (string parameter in listen.Arguments.Skip(1))
            {
                if (parameter == "ssl")
                {
                    ssl = true;
                }
                else if (parameter == "http2")
                {
                    reading.Http2 = true;
                }
                else if (!(v6 && parameter == "ipv6only=on"))
                {
                    context.Blockers.Add($"Line {listen.Line}: the listen parameter \"{parameter}\" is not something the wizard writes.");
                }
            }

            anySsl |= ssl;
            anyPlain |= !ssl;
            if (v6)
            {
                reading.Ipv6 = true;
                v6Port = port;
            }
            else if (reading.Port is { } known && known != port)
            {
                context.Blockers.Add($"Line {listen.Line}: the server listens on several ports ({known} and {port}).");
            }
            else
            {
                reading.Port = port;
            }
        }

        if (reading.Port is null)
        {
            context.Blockers.Add("A server block has no IPv4 \"listen\" directive.");
        }
        else if (v6Port is { } port6 && port6 != reading.Port)
        {
            context.Blockers.Add("The IPv4 and IPv6 listen directives use different ports.");
        }

        if (anySsl && anyPlain)
        {
            context.Blockers.Add("A server block mixes listen directives with and without ssl.");
        }

        reading.Tls = anySsl;
    }

    private static void ReadNames(List<NginxStatement> remaining, ServerReading reading, Context context)
    {
        foreach (NginxStatement statement in remaining.Where((NginxStatement item) => item.Name == "server_name").ToList())
        {
            remaining.Remove(statement);
            foreach (string name in statement.Arguments)
            {
                if (name.StartsWith('~') || name.Contains('$'))
                {
                    context.Blockers.Add($"Line {statement.Line}: the server name \"{name}\" is a pattern or a variable, which the wizard does not handle.");
                }
                else if (!reading.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    reading.Names.Add(name);
                }
            }
        }
    }

    private static void ReadTls(List<NginxStatement> remaining, ServerReading reading, Context context)
    {
        NginxStatement? http2 = remaining.FirstOrDefault((NginxStatement item) => item.Name == "http2" && item.Arguments.Count == 1);
        if (http2 is not null)
        {
            remaining.Remove(http2);
            reading.Http2 = http2.Arguments[0] == "on";
        }

        NginxStatement? certificate = remaining.FirstOrDefault((NginxStatement item) => item.Name == "ssl_certificate" && item.Arguments.Count == 1);
        if (certificate is not null)
        {
            remaining.Remove(certificate);
            reading.Certificate = certificate.Arguments[0];
            reading.Tls = true;
        }

        NginxStatement? key = remaining.FirstOrDefault((NginxStatement item) => item.Name == "ssl_certificate_key" && item.Arguments.Count == 1);
        if (key is not null)
        {
            remaining.Remove(key);
            reading.CertificateKey = key.Arguments[0];
        }

        foreach (NginxStatement protocols in remaining.Where((NginxStatement item) => item.Name == "ssl_protocols").ToList())
        {
            if (protocols.Arguments.SequenceEqual(["TLSv1.2", "TLSv1.3"]))
            {
                remaining.Remove(protocols);
            }
            else
            {
                context.Blockers.Add($"Line {protocols.Line}: ssl_protocols differs from \"TLSv1.2 TLSv1.3\", which the wizard always writes (nginx refuses the directive twice).");
            }
        }
    }

    private static void ReadAccess(List<NginxStatement> remaining, ServerReading reading, Context context)
    {
        string prefix = context.Locations.WhitelistIncludePath(string.Empty);
        List<NginxStatement> includes = remaining
            .Where((NginxStatement item) => item.Name == "include" && item.Arguments.Count == 1 && WhitelistName(item.Arguments[0], prefix) is not null)
            .ToList();
        NginxStatement? deny = remaining.FirstOrDefault((NginxStatement item) => item.Name == "deny" && item.Arguments.SequenceEqual(["all"]));
        if (includes.Count == 0 || deny is null)
        {
            return;
        }

        foreach (NginxStatement include in includes)
        {
            remaining.Remove(include);
            reading.Whitelists.Add(WhitelistName(include.Arguments[0], prefix)!);
        }

        remaining.Remove(deny);
        NginxStatement? other = remaining.FirstOrDefault((NginxStatement item) => item.Name is "allow" or "deny");
        if (other is not null)
        {
            context.Blockers.Add($"Line {other.Line}: \"{other.Name}\" next to a whitelist would end up after \"deny all\" and stop working, so the order of the rules would change.");
        }
    }

    private static string? WhitelistName(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string name = path[prefix.Length..];
        return ConfigNames.Normalize(ConfigArea.Whitelists, name) == name ? name : null;
    }

    private static void ReadLimitsAndHeaders(List<NginxStatement> remaining, ServerReading reading)
    {
        NginxStatement? limit = remaining.FirstOrDefault((NginxStatement item) => item.Name == "client_max_body_size" && item.Arguments.Count == 1);
        if (limit is not null)
        {
            remaining.Remove(limit);
            reading.MaxBodySize = limit.Arguments[0];
        }

        List<NginxStatement> securityHeaders = SecurityHeaderCanonicals
            .Select((string canonical) => remaining.FirstOrDefault((NginxStatement item) => item.Canonical == canonical))
            .OfType<NginxStatement>()
            .ToList();
        if (securityHeaders.Count == SecurityHeaderCanonicals.Length)
        {
            reading.SecurityHeaders = true;
            securityHeaders.ForEach((NginxStatement item) => remaining.Remove(item));
        }

        NginxStatement? hsts = remaining.FirstOrDefault((NginxStatement item) => item.Canonical == HstsCanonical);
        if (hsts is not null)
        {
            reading.Hsts = true;
            remaining.Remove(hsts);
        }

        NginxStatement? trace = remaining.FirstOrDefault((NginxStatement item) => item.Canonical == TraceCanonical);
        NginxStatement? dotfiles = remaining.FirstOrDefault((NginxStatement item) => item.Canonical == DotfilesCanonical);
        if (trace is not null && dotfiles is not null)
        {
            reading.BlockExploits = true;
            remaining.Remove(trace);
            remaining.Remove(dotfiles);
        }
    }

    private static void ReadLocations(List<NginxStatement> remaining, ServerReading reading, Context context)
    {
        NginxStatement? acme = remaining.FirstOrDefault((NginxStatement item) => IsAcmeLocation(item, context.AcmeWebroot));
        if (acme is not null)
        {
            reading.AcmeChallenge = true;
            remaining.Remove(acme);
        }

        NginxStatement? cache = remaining.FirstOrDefault((NginxStatement item) => item.Canonical == CacheCanonical);
        if (cache is not null)
        {
            reading.CacheAssets = true;
            remaining.Remove(cache);
        }

        NginxStatement? main = remaining.FirstOrDefault((NginxStatement item) => item.Name == "location" && item.IsBlock && item.Arguments.SequenceEqual(["/"]));
        if (main is not null)
        {
            reading.MainLocation = main;
            remaining.Remove(main);
        }

        NginxStatement? root = remaining.FirstOrDefault((NginxStatement item) => item.Name == "root" && item.Arguments.Count == 1);
        if (root is not null && main is not null && main.Children!.Any((NginxStatement item) => item.Name == "try_files"))
        {
            reading.Root = root.Arguments[0];
            remaining.Remove(root);
        }
    }

    private static bool IsAcmeLocation(NginxStatement statement, string webroot)
    {
        if (statement.Name != "location" || !statement.Arguments.SequenceEqual(["^~", "/.well-known/acme-challenge/"]))
        {
            return false;
        }

        IReadOnlyList<NginxStatement> children = statement.Children ?? [];
        bool rootMatches = children.Any((NginxStatement item) => item.Name == "root" && item.Arguments.Count == 1 && item.Arguments[0].TrimEnd('/') == webroot);
        return rootMatches && children.All((NginxStatement item) => item.Name == "root" || item.Canonical == "allow all;");
    }

    /// <summary>The HTTP server of a pair must do nothing but redirect (the wizard writes the same server again from its settings).</summary>
    private static void CheckRedirect(ServerReading redirect, ServerReading main, List<string> blockers)
    {
        if (!redirect.Names.OrderBy((string name) => name, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(main.Names.OrderBy((string name) => name, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
        {
            blockers.Add("The HTTP and the HTTPS server blocks do not have the same server names.");
        }

        string authority = main.Port == 443 ? "$host" : $"$host:{main.Port}";
        string expected = $"https://{authority}$request_uri";
        List<NginxStatement> rest = redirect.Rest.ToList();
        if (redirect.MainLocation is { } location)
        {
            if (rest.Count > 0 || location.Children!.Count != 1)
            {
                blockers.Add("The HTTP server block does more than redirect to HTTPS, which the wizard cannot reproduce.");
                return;
            }

            rest = location.Children!.ToList();
        }

        bool isRedirect = rest.Count == 1
            && rest[0].Name == "return"
            && rest[0].Arguments.Count == 2
            && rest[0].Arguments[0] == "301"
            && rest[0].Arguments[1] == expected;
        if (!isRedirect || redirect.Root is not null)
        {
            blockers.Add($"The HTTP server block must only contain \"return 301 {expected};\" (the wizard writes exactly that), possibly with the Let's Encrypt challenge location.");
        }
    }

    private static SiteSpec BuildSpec(ServerReading main, ServerReading? redirect, List<string> blockers, List<string> notes)
    {
        bool proxy = main.MainLocation?.Children?.Any((NginxStatement item) => item.Name == "proxy_pass") ?? false;
        List<NginxStatement> locationRest = main.MainLocation?.Children?.ToList() ?? [];
        string upstream = string.Empty;
        bool websockets = false;
        int? timeout = null;
        bool spa = false;

        if (main.MainLocation is null)
        {
            blockers.Add("There is no \"location /\" block: the wizard builds its site around one that proxies or serves files.");
        }
        else if (proxy)
        {
            ReadProxyLocation(locationRest, blockers, notes, out upstream, out websockets, out timeout);
        }
        else if (!ReadStaticLocation(main, locationRest, blockers, out spa))
        {
            blockers.Add("The main location neither proxies (proxy_pass) nor serves files from a root with try_files.");
        }

        if (!proxy && main.Root is null && blockers.Count == 0)
        {
            blockers.Add("The static site has no \"root\" directive at server level.");
        }

        string? extraServer = JoinText(main.Rest);
        string? extraLocation = JoinText(locationRest);
        return new SiteSpec
        {
            Kind = proxy ? SiteKind.ReverseProxy : SiteKind.StaticSite,
            ServerNames = main.Names,
            HttpPort = redirect?.Port ?? main.Port ?? 80,
            Ipv6 = main.Ipv6 || (redirect?.Ipv6 ?? false),
            Upstream = upstream,
            Websockets = !proxy || websockets,
            ProxyTimeoutSeconds = timeout,
            RootDirectory = main.Root ?? string.Empty,
            SpaFallback = spa,
            CacheStaticAssets = proxy || main.CacheAssets,
            EnableTls = main.Tls,
            HttpsPort = main.Tls ? main.Port ?? 443 : 443,
            CertificatePath = main.Certificate,
            CertificateKeyPath = main.CertificateKey,
            // Without TLS these are not written; they keep the wizard's defaults, which is what a later "enable HTTPS" starts from.
            ForceHttps = redirect is not null || !main.Tls,
            Http2 = main.Http2 || !main.Tls,
            Hsts = main.Hsts,
            MaxBodySize = main.MaxBodySize,
            SecurityHeaders = main.SecurityHeaders,
            BlockCommonExploits = main.BlockExploits,
            Whitelists = main.Whitelists.OrderBy((string name) => name, StringComparer.Ordinal).ToList(),
            ServeAcmeChallenge = main.AcmeChallenge || (redirect?.AcmeChallenge ?? false),
            ExtraServerDirectives = extraServer,
            ExtraLocationDirectives = extraLocation,
        };
    }

    private static string? JoinText(IEnumerable<NginxStatement> statements)
    {
        string text = string.Join('\n', statements.Select((NginxStatement statement) => statement.Text));
        return text.Length == 0 ? null : text;
    }

    private static void ReadProxyLocation(List<NginxStatement> rest, List<string> blockers, List<string> notes, out string upstream, out bool websockets, out int? timeout)
    {
        NginxStatement pass = rest.First((NginxStatement item) => item.Name == "proxy_pass");
        upstream = pass.Arguments.Count == 1 ? pass.Arguments[0] : string.Empty;
        rest.Remove(pass);
        List<string> missing = [];

        NginxStatement? version = rest.FirstOrDefault((NginxStatement item) => item.Name == "proxy_http_version");
        if (version is null)
        {
            missing.Add("proxy_http_version 1.1");
        }
        else if (version.Arguments.SequenceEqual(["1.1"]))
        {
            rest.Remove(version);
        }
        else
        {
            blockers.Add($"Line {version.Line}: proxy_http_version is not 1.1, which the wizard always writes.");
        }

        NginxStatement? sni = rest.FirstOrDefault((NginxStatement item) => item.Name == "proxy_ssl_server_name" && item.Arguments.SequenceEqual(["on"]));
        if (sni is not null && upstream.StartsWith("https://", StringComparison.Ordinal))
        {
            rest.Remove(sni);
        }
        else if (sni is null && upstream.StartsWith("https://", StringComparison.Ordinal))
        {
            missing.Add("proxy_ssl_server_name on");
        }

        foreach (KeyValuePair<string, string> header in DefaultProxyHeaders)
        {
            List<NginxStatement> sameName = rest
                .Where((NginxStatement item) => item.Name == "proxy_set_header" && item.Arguments.Count == 2 && string.Equals(item.Arguments[0], header.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameName.Count == 0)
            {
                missing.Add($"proxy_set_header {header.Key}");
            }

            foreach (NginxStatement statement in sameName)
            {
                if (statement.Arguments[1] == header.Value)
                {
                    rest.Remove(statement);
                }
                else
                {
                    blockers.Add($"Line {statement.Line}: \"proxy_set_header {header.Key} {statement.Arguments[1]}\" differs from what the wizard always writes ({header.Value}).");
                }
            }
        }

        NginxStatement? upgrade = rest.FirstOrDefault((NginxStatement item) => item.Name == "proxy_set_header" && item.Arguments.Count == 2 && item.Arguments[0].Equals("Upgrade", StringComparison.OrdinalIgnoreCase));
        NginxStatement? connection = rest.FirstOrDefault((NginxStatement item) => item.Name == "proxy_set_header" && item.Arguments.Count == 2 && item.Arguments[0].Equals("Connection", StringComparison.OrdinalIgnoreCase));
        websockets = false;
        if (upgrade is not null && upgrade.Arguments[1] == "$http_upgrade" && connection is not null
            && (connection.Arguments[1] == "upgrade" || connection.Arguments[1].StartsWith('$')))
        {
            websockets = true;
            rest.Remove(upgrade);
            rest.Remove(connection);
        }
        else if (connection is not null && connection.Arguments[1] != "upgrade")
        {
            blockers.Add($"Line {connection.Line}: \"proxy_set_header Connection {connection.Arguments[1]}\" cannot be turned into the wizard's WebSocket switch.");
        }

        timeout = null;
        NginxStatement? read = rest.FirstOrDefault((NginxStatement item) => item.Name == "proxy_read_timeout" && item.Arguments.Count == 1);
        NginxStatement? send = rest.FirstOrDefault((NginxStatement item) => item.Name == "proxy_send_timeout" && item.Arguments.Count == 1);
        if (read is not null && send is not null
            && Seconds(read.Arguments[0]) is { } seconds && Seconds(send.Arguments[0]) == seconds)
        {
            timeout = seconds;
            rest.Remove(read);
            rest.Remove(send);
        }

        if (missing.Count > 0)
        {
            notes.Add("The wizard always writes " + string.Join(", ", missing) + ", which this file did not have. Check that your application is fine with it.");
        }
    }

    private static int? Seconds(string value)
    {
        Match match = DurationPattern().Match(value);
        if (!match.Success)
        {
            return null;
        }

        int number = int.Parse(match.Groups["number"].Value);
        int seconds = match.Groups["unit"].Value == "m" ? number * 60 : number;
        return seconds is >= 1 and <= 3600 ? seconds : null;
    }

    private static bool ReadStaticLocation(ServerReading main, List<NginxStatement> rest, List<string> blockers, out bool spa)
    {
        spa = false;
        NginxStatement? tryFiles = rest.FirstOrDefault((NginxStatement item) => item.Name == "try_files");
        if (tryFiles is null || main.Root is null)
        {
            return false;
        }

        if (tryFiles.Arguments.SequenceEqual(["$uri", "$uri/", "=404"]))
        {
            spa = false;
        }
        else if (tryFiles.Arguments.SequenceEqual(["$uri", "$uri/", "/index.html"]))
        {
            spa = true;
        }
        else
        {
            blockers.Add($"Line {tryFiles.Line}: try_files is neither \"$uri $uri/ =404\" nor the single page application form \"$uri $uri/ /index.html\".");
            return true;
        }

        rest.Remove(tryFiles);
        NginxStatement? index = main.Rest.FirstOrDefault((NginxStatement item) => item.Name == "index");
        if (index is not null)
        {
            if (index.Arguments.SequenceEqual(["index.html", "index.htm"]))
            {
                main.Rest.Remove(index);
            }
            else
            {
                blockers.Add($"Line {index.Line}: the index files differ from \"index.html index.htm\", which the wizard always writes.");
            }
        }

        return true;
    }
}
