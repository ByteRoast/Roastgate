using Roastgate.Core.Nginx;
using Roastgate.Core.Nginx.Generation;
using Roastgate.Core.Settings;

namespace Roastgate.Tests.Generation;

public sealed class SiteGeneratorTests
{
    private static readonly AppSettings Settings = new();
    private static readonly Version Modern = new(1, 27, 0);

    private static GenerationContext Context(string fileName = "app.example.com.conf", Version? nginx = null)
    {
        return new GenerationContext(new ConfigLocations(Settings), Settings.AcmeWebroot, fileName, nginx);
    }

    private static string Generate(SiteSpec spec, string fileName = "app.example.com.conf", Version? nginx = null)
    {
        return SiteGenerator.Generate(spec, Context(fileName, nginx));
    }

    private static SiteSpec HttpsProxy()
    {
        return new SiteSpec
        {
            ServerNames = ["a.example.com"],
            Upstream = "http://127.0.0.1:3000",
            EnableTls = true,
            CertificatePath = "/c.pem",
            CertificateKeyPath = "/k.pem",
        };
    }

    // ---- Reference outputs (reviewed by eye, then pinned) -----------------------------

    [Fact]
    public void A_default_reverse_proxy_over_http()
    {
        string text = Generate(new SiteSpec { ServerNames = ["app.example.com"], Upstream = "http://127.0.0.1:3000" });

        Assert.Equal(
            """
                map $http_upgrade $rg_upgrade_app_example_com {
                    default upgrade;
                    ''      close;
                }

                server {
                    listen 80;
                    listen [::]:80;
                    server_name app.example.com;

                    add_header X-Content-Type-Options nosniff always;
                    add_header X-Frame-Options SAMEORIGIN always;
                    add_header Referrer-Policy strict-origin-when-cross-origin always;
                    if ($request_method ~* "^(TRACE|TRACK)$") { return 405; }
                    location ~ /\.(?!well-known) {
                        deny all;
                    }

                    location / {
                        proxy_pass http://127.0.0.1:3000;
                        proxy_http_version 1.1;
                        proxy_set_header Host $host;
                        proxy_set_header X-Real-IP $remote_addr;
                        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                        proxy_set_header X-Forwarded-Proto $scheme;
                        proxy_set_header X-Forwarded-Host $host;
                        proxy_set_header Upgrade $http_upgrade;
                        proxy_set_header Connection $rg_upgrade_app_example_com;
                    }
                }

                """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void An_https_reverse_proxy_with_redirect_whitelists_and_acme()
    {
        string text = Generate(new SiteSpec
        {
            ServerNames = ["app.example.com", "www.example.com"],
            Upstream = "https://backend:8443",
            EnableTls = true,
            CertificatePath = "/etc/letsencrypt/live/app/fullchain.pem",
            CertificateKeyPath = "/etc/letsencrypt/live/app/privkey.pem",
            Hsts = true,
            ServeAcmeChallenge = true,
            Whitelists = ["alice.conf", "bob.conf"],
            MaxBodySize = "100m",
            ProxyTimeoutSeconds = 120,
            ExtraLocationDirectives = "proxy_buffering off;",
        }, nginx: Modern);

        Assert.Equal(
            """
                map $http_upgrade $rg_upgrade_app_example_com {
                    default upgrade;
                    ''      close;
                }

                server {
                    listen 80;
                    listen [::]:80;
                    server_name app.example.com www.example.com;

                    include /etc/nginx/whitelists/alice.conf;
                    include /etc/nginx/whitelists/bob.conf;
                    deny all;

                    location ^~ /.well-known/acme-challenge/ {
                        allow all;
                        root /var/www/certbot;
                    }

                    location / {
                        return 301 https://$host$request_uri;
                    }
                }

                server {
                    listen 443 ssl;
                    listen [::]:443 ssl;
                    http2 on;
                    server_name app.example.com www.example.com;

                    ssl_certificate /etc/letsencrypt/live/app/fullchain.pem;
                    ssl_certificate_key /etc/letsencrypt/live/app/privkey.pem;
                    ssl_protocols TLSv1.2 TLSv1.3;

                    include /etc/nginx/whitelists/alice.conf;
                    include /etc/nginx/whitelists/bob.conf;
                    deny all;

                    client_max_body_size 100m;
                    add_header X-Content-Type-Options nosniff always;
                    add_header X-Frame-Options SAMEORIGIN always;
                    add_header Referrer-Policy strict-origin-when-cross-origin always;
                    add_header Strict-Transport-Security "max-age=31536000" always;
                    if ($request_method ~* "^(TRACE|TRACK)$") { return 405; }
                    location ~ /\.(?!well-known) {
                        deny all;
                    }

                    location ^~ /.well-known/acme-challenge/ {
                        allow all;
                        root /var/www/certbot;
                    }

                    location / {
                        proxy_pass https://backend:8443;
                        proxy_http_version 1.1;
                        proxy_ssl_server_name on;
                        proxy_set_header Host $host;
                        proxy_set_header X-Real-IP $remote_addr;
                        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                        proxy_set_header X-Forwarded-Proto $scheme;
                        proxy_set_header X-Forwarded-Host $host;
                        proxy_set_header Upgrade $http_upgrade;
                        proxy_set_header Connection $rg_upgrade_app_example_com;
                        proxy_read_timeout 120s;
                        proxy_send_timeout 120s;
                        proxy_buffering off;
                    }
                }

                """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void A_single_page_application_served_as_a_static_site()
    {
        string text =
            Generate(
                new SiteSpec
                {
                    Kind = SiteKind.StaticSite, ServerNames = ["docs.example.com"], RootDirectory = "/var/www/docs",
                    SpaFallback = true
                }, "docs.example.com");

        Assert.Equal(
            """
                server {
                    listen 80;
                    listen [::]:80;
                    server_name docs.example.com;

                    add_header X-Content-Type-Options nosniff always;
                    add_header X-Frame-Options SAMEORIGIN always;
                    add_header Referrer-Policy strict-origin-when-cross-origin always;
                    if ($request_method ~* "^(TRACE|TRACK)$") { return 405; }
                    location ~ /\.(?!well-known) {
                        deny all;
                    }

                    root /var/www/docs;
                    index index.html index.htm;

                    location / {
                        try_files $uri $uri/ /index.html;
                    }

                    location ~* \.(?:css|js|mjs|jpg|jpeg|gif|png|svg|ico|webp|avif|woff2?)$ {
                        expires 30d;
                        try_files $uri =404;
                    }
                }

                """.ReplaceLineEndings("\n"),
            text);
    }

    // ---- Behaviour -------------------------------------------------------------------

    [Fact]
    public void Nginx_1_25_1_and_later_get_the_http2_directive()
    {
        string text = Generate(HttpsProxy(), nginx: Modern);

        Assert.Contains("    http2 on;\n", text);
        Assert.Contains("listen 443 ssl;", text);
        Assert.DoesNotContain("ssl http2", text);
    }

    [Theory]
    [InlineData("1.18.0")]
    [InlineData("1.24.0")]
    [InlineData("1.25.0")]
    public void Older_nginx_gets_http2_as_a_listen_parameter_because_the_directive_would_make_nginx_t_fail(
        string version)
    {
        string text = Generate(HttpsProxy(), nginx: Version.Parse(version));

        Assert.Contains("listen 443 ssl http2;", text);
        Assert.Contains("listen [::]:443 ssl http2;", text);
        Assert.DoesNotContain("http2 on;", text);
    }

    [Fact]
    public void When_the_version_is_unknown_the_syntax_that_works_everywhere_is_used()
    {
        string text = Generate(HttpsProxy(), nginx: null);

        Assert.Contains("listen 443 ssl http2;", text);
        Assert.DoesNotContain("http2 on;", text);
    }

    [Theory]
    [InlineData("1.25.1")]
    [InlineData("1.26.2")]
    [InlineData("1.28.0")]
    [InlineData("2.0.0")]
    public void The_http2_directive_starts_at_1_25_1(string version)
    {
        Assert.Contains("http2 on;", Generate(HttpsProxy(), nginx: Version.Parse(version)));
    }

    [Theory]
    [InlineData("1.24.0")]
    [InlineData("1.27.0")]
    public void Without_http2_neither_form_appears(string version)
    {
        string text = Generate(HttpsProxy() with { Http2 = false }, nginx: Version.Parse(version));

        Assert.DoesNotContain("http2", text);
    }

    [Fact]
    public void Ipv6_listeners_can_be_left_out_for_hosts_without_ipv6()
    {
        string withIpv6 = Generate(HttpsProxy());
        string withoutIpv6 = Generate(HttpsProxy() with { Ipv6 = false });

        Assert.Equal(2, Count(withIpv6, "listen [::]:"));
        Assert.DoesNotContain("[::]", withoutIpv6);
        Assert.Equal(2, Count(withoutIpv6, "listen "));
    }

    [Fact]
    public void With_everything_optional_switched_off_the_output_is_minimal()
    {
        string text = Generate(new SiteSpec
        {
            ServerNames = ["a.example.com"],
            Upstream = "http://127.0.0.1:3000",
            Websockets = false,
            SecurityHeaders = false,
            BlockCommonExploits = false,
        });

        Assert.DoesNotContain("map ", text);
        Assert.DoesNotContain("add_header", text);
        Assert.DoesNotContain("TRACE", text);
        Assert.DoesNotContain("Upgrade", text);
        Assert.Equal(1, Count(text, "server {"));
    }

    [Fact]
    public void Https_without_the_redirect_serves_the_site_on_both_ports()
    {
        string text = Generate(new SiteSpec
        {
            ServerNames = ["a.example.com"],
            Upstream = "http://127.0.0.1:3000",
            EnableTls = true,
            ForceHttps = false,
            CertificatePath = "/c.pem",
            CertificateKeyPath = "/k.pem",
        });

        Assert.Equal(2, Count(text, "server {"));
        Assert.Equal(2, Count(text, "proxy_pass "));
        Assert.DoesNotContain("return 301", text);
    }

    [Fact]
    public void A_custom_https_port_appears_in_the_redirect()
    {
        string text = Generate(new SiteSpec
        {
            ServerNames = ["a.example.com"],
            Upstream = "http://127.0.0.1:3000",
            EnableTls = true,
            HttpPort = 8080,
            HttpsPort = 8443,
            CertificatePath = "/c.pem",
            CertificateKeyPath = "/k.pem",
        }, nginx: Modern);

        Assert.Contains("listen 8080;", text);
        Assert.Contains("listen 8443 ssl;", text);
        Assert.Contains("return 301 https://$host:8443$request_uri;", text);
    }

    [Fact]
    public void Hsts_is_only_sent_over_https()
    {
        string text = Generate(new SiteSpec
        {
            ServerNames = ["a.example.com"],
            Upstream = "http://127.0.0.1:3000",
            EnableTls = true,
            ForceHttps = false,
            Hsts = true,
            CertificatePath = "/c.pem",
            CertificateKeyPath = "/k.pem",
        });

        Assert.Equal(1, Count(text, "Strict-Transport-Security"));

        int hsts = text.IndexOf("Strict-Transport-Security", StringComparison.Ordinal);
        int httpsServer = text.IndexOf("listen 443 ssl;", StringComparison.Ordinal);

        Assert.True(hsts > httpsServer, "HSTS must be inside the HTTPS server block");
    }

    [Fact]
    public void The_acme_challenge_is_only_open_when_a_whitelist_would_otherwise_block_it()
    {
        string withoutWhitelist = Generate(new SiteSpec
            { ServerNames = ["a.example.com"], Upstream = "http://127.0.0.1:3000", ServeAcmeChallenge = true });

        string withWhitelist = Generate(new SiteSpec
        {
            ServerNames = ["a.example.com"], Upstream = "http://127.0.0.1:3000", ServeAcmeChallenge = true,
            Whitelists = ["alice.conf"]
        });

        Assert.DoesNotContain("allow all;", withoutWhitelist);
        Assert.Contains("allow all;", withWhitelist);
    }

    [Theory]
    [InlineData(DeniedBehavior.NotFound, null, "return 404;")]
    [InlineData(DeniedBehavior.CloseConnection, null, "return 444;")]
    [InlineData(DeniedBehavior.Redirect, "https://example.com/?from=app", "return 302 https://example.com/?from=app;")]
    public void Refused_visitors_are_sent_to_a_named_location_that_applies_the_behavior(DeniedBehavior behavior,
        string? url, string directive)
    {
        string text = Generate(new SiteSpec
        {
            ServerNames = ["a.example.com"],
            Upstream = "http://127.0.0.1:3000",
            Whitelists = ["alice.conf"],
            DeniedBehavior = behavior,
            DeniedUrl = url,
        });

        // "allow all" is what keeps the inherited "deny all" from refusing the named location itself.
        Assert.Contains(
            $$"""
                      include /etc/nginx/whitelists/alice.conf;
                      deny all;

                      error_page 403 = @rg_denied;

                      location @rg_denied {
                          allow all;
                          {{directive}}
                      }

                  """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void A_forbidden_visitor_gets_the_plain_403_without_a_named_location()
    {
        string text = Generate(new SiteSpec
            { ServerNames = ["a.example.com"], Upstream = "http://127.0.0.1:3000", Whitelists = ["alice.conf"] });

        Assert.Contains("deny all;", text);
        Assert.DoesNotContain("error_page", text);
        Assert.DoesNotContain("@rg_denied", text);
    }

    [Fact]
    public void The_denied_behavior_needs_a_whitelist_to_be_written()
    {
        string text = Generate(new SiteSpec
        {
            ServerNames = ["a.example.com"], Upstream = "http://127.0.0.1:3000",
            DeniedBehavior = DeniedBehavior.NotFound
        });

        Assert.DoesNotContain("error_page", text);
        Assert.DoesNotContain("@rg_denied", text);
    }

    [Fact]
    public void The_denied_behavior_also_applies_to_the_http_server_that_redirects_to_https()
    {
        string text = Generate(HttpsProxy() with
        {
            Whitelists = ["alice.conf"], DeniedBehavior = DeniedBehavior.CloseConnection
        });

        Assert.Equal(2, Count(text, "error_page 403 = @rg_denied;"));
        Assert.Equal(2, Count(text, "location @rg_denied {"));

        int namedLocation = text.IndexOf("location @rg_denied {", StringComparison.Ordinal);
        int redirect = text.IndexOf("return 301 https://", StringComparison.Ordinal);

        Assert.True(namedLocation < redirect, "the HTTP server must refuse before it redirects");
    }

    [Fact]
    public void Free_directives_are_indented_and_keep_their_own_structure()
    {
        string text = Generate(new SiteSpec
        {
            ServerNames = ["a.example.com"],
            Upstream = "http://127.0.0.1:3000",
            ExtraServerDirectives = "location /health {\r\n\treturn 200 \"ok\";\r\n}\r\n",
            ExtraLocationDirectives = "proxy_buffering off;",
        });

        Assert.Contains("    location /health {\n        return 200 \"ok\";\n    }\n", text);
        Assert.Contains("        proxy_buffering off;\n", text);
        Assert.DoesNotContain("\r", text);
    }

    [Theory]
    [InlineData("app.example.com.conf", "$rg_upgrade_app_example_com")]
    [InlineData("My-App_2.conf", "$rg_upgrade_my_app_2")]
    [InlineData("example.com", "$rg_upgrade_example_com")]
    public void The_websocket_variable_is_named_after_the_file_so_two_proxies_never_clash(string fileName,
        string variable)
    {
        string text = Generate(new SiteSpec { ServerNames = ["a.example.com"], Upstream = "http://127.0.0.1:3000" },
            fileName);

        Assert.Contains($"map $http_upgrade {variable} {{", text);
        Assert.Contains($"proxy_set_header Connection {variable};", text);
    }

    [Fact]
    public void The_output_never_contains_carriage_returns_tabs_or_double_blank_lines()
    {
        string text = Generate(new SiteSpec
        {
            ServerNames = ["a.example.com"],
            Upstream = "http://127.0.0.1:3000",
            EnableTls = true,
            CertificatePath = "/c.pem",
            CertificateKeyPath = "/k.pem",
            Whitelists = ["a.conf"],
            ServeAcmeChallenge = true,
            ExtraServerDirectives = "\n\n# note\n\n",
        });

        Assert.DoesNotContain("\r", text);
        Assert.DoesNotContain("\t", text);
        Assert.DoesNotContain("\n\n\n", text);
        Assert.EndsWith("}\n", text);
    }

    // ---- Properties over every combination of switches --------------------------------

    public static IEnumerable<object[]> SwitchCombinations()
    {
        foreach (SiteKind kind in Enum.GetValues<SiteKind>())
        {
            for (int bits = 0; bits < 128; bits++)
            {
                yield return [kind, bits];
            }
        }
    }

    private static SiteSpec FromBits(SiteKind kind, int bits)
    {
        bool Bit(int position)
        {
            return (bits & (1 << position)) != 0;
        }

        return new SiteSpec
        {
            Kind = kind,
            ServerNames = ["a.example.com", "b.example.com"],
            Upstream = "http://127.0.0.1:3000",
            RootDirectory = "/var/www/a",
            Websockets = Bit(0),
            EnableTls = Bit(1),
            ForceHttps = Bit(2),
            Hsts = Bit(3),
            SecurityHeaders = Bit(4),
            BlockCommonExploits = Bit(5),
            ServeAcmeChallenge = Bit(6),
            Whitelists = Bit(3) ? ["alice.conf"] : [],
            SpaFallback = Bit(0),
            CacheStaticAssets = Bit(4),
            CertificatePath = "/c.pem",
            CertificateKeyPath = "/k.pem",
            MaxBodySize = Bit(5) ? "10m" : null,
            ProxyTimeoutSeconds = Bit(6) ? 30 : null,
            ExtraServerDirectives = Bit(2) ? "add_header X-A b;" : null,
            ExtraLocationDirectives = Bit(1) ? "add_header X-B c;" : null,
        };
    }

    [Theory]
    [MemberData(nameof(SwitchCombinations))]
    public void Every_combination_of_switches_gives_a_valid_spec_and_well_formed_nginx(SiteKind kind, int bits)
    {
        SiteSpec spec = FromBits(kind, bits);

        Assert.Empty(SiteSpecValidator.Validate(spec with { Hsts = spec.EnableTls && spec.Hsts }));

        spec = spec with { Hsts = spec.EnableTls && spec.Hsts };

        string text = Generate(spec);

        Assert.True(SiteSpecValidator.BracesBalanced(text), "braces must balance");

        int servers = spec.EnableTls ? 2 : 1;

        Assert.Equal(servers, Count(text, "server_name a.example.com b.example.com;"));
        Assert.Equal(servers, Count(text, "server {"));

        foreach (string line in text.Split('\n'))
        {
            Assert.True(line.TrimEnd() == line, $"trailing whitespace in \"{line}\"");
        }

        if (spec.Kind == SiteKind.ReverseProxy)
        {
            Assert.Contains("proxy_pass http://127.0.0.1:3000;", text);
            Assert.DoesNotContain("try_files", text);
        }
        else
        {
            Assert.Contains("root /var/www/a;", text);
            Assert.DoesNotContain("proxy_pass", text);
            Assert.DoesNotContain("map $http_upgrade", text);
        }
    }

    private static SiteSpec FileServer(FancyIndexOptions? options)
    {
        return new SiteSpec
        {
            Kind = SiteKind.StaticSite,
            ServerNames = ["files.example.com"],
            RootDirectory = "/srv/files",
            BlockCommonExploits = false,
            FancyIndex = options,
        };
    }

    [Fact]
    public void Writes_the_fancyindex_directives_in_the_main_location()
    {
        string text = Generate(FileServer(new FancyIndexOptions
            {
                ExactSize = false,
                LocalTime = true,
                Header = "/.nginxy/header.html",
                Footer = "/.nginxy/footer.html",
                CssHref = "/.nginxy/style.css",
                Ignore = ["radarr", "uploads"],
                TimeFormat = "%d-%b-%Y %H:%M",
            }) with
            {
                ExtraLocationDirectives = "add_header Cache-Control no-store;"
            });

        Assert.Contains("fancyindex on;", text);
        Assert.Contains("fancyindex_exact_size off;", text);
        Assert.Contains("fancyindex_localtime on;", text);
        Assert.Contains("fancyindex_show_path on;", text);
        Assert.Contains("fancyindex_directories_first on;", text);
        Assert.Contains("fancyindex_case_sensitive on;", text);
        Assert.Contains("fancyindex_show_dotfiles off;", text);
        Assert.Contains("fancyindex_hide_symlinks off;", text);
        Assert.Contains("fancyindex_hide_parent_dir off;", text);
        Assert.Contains("fancyindex_time_format \"%d-%b-%Y %H:%M\";", text);
        Assert.Contains("fancyindex_header \"/.nginxy/header.html\";", text);
        Assert.Contains("fancyindex_footer \"/.nginxy/footer.html\";", text);
        Assert.Contains("fancyindex_css_href \"/.nginxy/style.css\";", text);
        Assert.Contains("fancyindex_ignore radarr uploads;", text);

        // Inside "location /", after try_files and before the free directives.
        int tryFiles = text.IndexOf("try_files $uri $uri/ =404;", StringComparison.Ordinal);
        int fancyIndex = text.IndexOf("fancyindex on;", StringComparison.Ordinal);
        int extra = text.IndexOf("add_header Cache-Control no-store;", StringComparison.Ordinal);

        Assert.True(tryFiles >= 0 && tryFiles < fancyIndex && fancyIndex < extra);
    }

    [Theory]
    [InlineData(FancyIndexSort.Name, "name")]
    [InlineData(FancyIndexSort.NameDescending, "name_desc")]
    [InlineData(FancyIndexSort.Size, "size")]
    [InlineData(FancyIndexSort.SizeDescending, "size_desc")]
    [InlineData(FancyIndexSort.Date, "date")]
    [InlineData(FancyIndexSort.DateDescending, "date_desc")]
    public void Writes_the_sort_order_the_way_the_module_expects_it(FancyIndexSort sort, string expected)
    {
        string text = Generate(
            FileServer(
                new FancyIndexOptions
                {
                    DefaultSort = sort
                }
            )
        );

        Assert.Contains($"fancyindex_default_sort {expected};", text);
    }

    [Fact]
    public void Leaves_out_the_header_footer_and_ignore_lines_when_there_is_nothing_to_write()
    {
        string text = Generate(FileServer(new FancyIndexOptions()));

        Assert.Contains("fancyindex on;", text);
        Assert.DoesNotContain("fancyindex_header", text);
        Assert.DoesNotContain("fancyindex_footer", text);
        Assert.DoesNotContain("fancyindex_css_href", text);
        Assert.DoesNotContain("fancyindex_ignore", text);
    }

    [Fact]
    public void Writes_nothing_about_fancyindex_without_options()
    {
        string text = Generate(FileServer(null));

        Assert.DoesNotContain("fancyindex", text);
    }

    [Fact]
    public void Lets_the_header_and_footer_through_the_exploit_protection_when_they_are_in_a_dot_directory()
    {
        SiteSpec spec = FileServer(new FancyIndexOptions
            {
                Header = "/.nginxy/header.html",
                Footer = "/.nginxy/footer.html",
            }) with
            {
                BlockCommonExploits = true
            };

        string text = Generate(spec);

        Assert.Equal(1, Count(text, "location ^~ /.nginxy/ {"));
        Assert.Contains("try_files $uri =404;", text);
    }

    [Fact]
    public void Adds_no_exception_for_a_directory_without_a_dot()
    {
        SiteSpec spec = FileServer(
                new FancyIndexOptions
                {
                    Header = "/assets/header.html"
                }
            ) with
            {
                BlockCommonExploits = true
            };

        string text = Generate(spec);

        Assert.DoesNotContain("location ^~", text);
    }

    [Fact]
    public void Adds_no_exception_when_the_exploit_protection_is_off()
    {
        SiteSpec spec = FileServer(
                new FancyIndexOptions
                {
                    Header = "/.nginxy/header.html"
                }
            )
            with
            {
                BlockCommonExploits = false
            };

        string text = Generate(spec);

        Assert.DoesNotContain("location ^~", text);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        int index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
