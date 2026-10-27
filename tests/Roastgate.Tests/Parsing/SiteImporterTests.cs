using Roastgate.Core.Nginx;
using Roastgate.Core.Nginx.Generation;
using Roastgate.Core.Nginx.Parsing;
using Roastgate.Core.Settings;

namespace Roastgate.Tests.Parsing;

public sealed class SiteImporterTests
{
    private static readonly AppSettings Settings = new();

    private static SiteImportResult Import(string content)
    {
        return SiteImporter.Import(content, new ConfigLocations(Settings), Settings.AcmeWebroot);
    }

    private static SiteSpec ImportOk(string content)
    {
        SiteImportResult result = Import(content);
        Assert.True(result.Succeeded, string.Join(" | ", result.Blockers));
        return result.Spec!;
    }

    private static string Wrap(string body)
    {
        return "server {\n    listen 80;\n    server_name app.example.com;\n" + body + "}\n";
    }

    private const string ProxyLocation = """
            location / {
                proxy_pass http://127.0.0.1:3000;
            }

        """;

    [Fact]
    public void A_plain_http_proxy_is_read()
    {
        SiteSpec spec = ImportOk(Wrap(ProxyLocation));

        Assert.Equal(SiteKind.ReverseProxy, spec.Kind);
        Assert.Equal(["app.example.com"], spec.ServerNames);
        Assert.Equal("http://127.0.0.1:3000", spec.Upstream);
        Assert.Equal(80, spec.HttpPort);
        Assert.False(spec.EnableTls);
        Assert.False(spec.Ipv6);
        Assert.False(spec.Websockets);
        Assert.False(spec.SecurityHeaders);
        Assert.False(spec.BlockCommonExploits);
        Assert.Null(spec.ExtraServerDirectives);
        Assert.Null(spec.ExtraLocationDirectives);
    }

    [Fact]
    public void The_defaults_the_wizard_adds_are_reported_as_a_note()
    {
        SiteImportResult result = Import(Wrap(ProxyLocation));

        Assert.Contains(result.Notes, (string note) => note.Contains("proxy_set_header Host", StringComparison.Ordinal));
    }

    [Fact]
    public void Websockets_a_timeout_and_the_standard_headers_are_recognised()
    {
        SiteSpec spec = ImportOk(Wrap("""
            location / {
                proxy_pass http://127.0.0.1:3000;
                proxy_http_version 1.1;
                proxy_set_header Host $host;
                proxy_set_header X-Real-IP $remote_addr;
                proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                proxy_set_header X-Forwarded-Proto $scheme;
                proxy_set_header X-Forwarded-Host $host;
                proxy_set_header Upgrade $http_upgrade;
                proxy_set_header Connection "upgrade";
                proxy_read_timeout 300s;
                proxy_send_timeout 5m;
            }

        """));

        Assert.True(spec.Websockets);
        Assert.Equal(300, spec.ProxyTimeoutSeconds);
        Assert.Null(spec.ExtraLocationDirectives);
    }

    [Fact]
    public void Unknown_location_directives_are_kept_verbatim()
    {
        SiteSpec spec = ImportOk(Wrap("""
            location / {
                proxy_pass http://127.0.0.1:3000;
                proxy_buffering off;
                proxy_set_header X-Custom "a b";
            }

        """));

        Assert.Equal("proxy_buffering off;\nproxy_set_header X-Custom \"a b\";", spec.ExtraLocationDirectives);
    }

    [Fact]
    public void Unknown_server_directives_and_other_locations_are_kept_verbatim()
    {
        SiteSpec spec = ImportOk(Wrap("""
            gzip on;
            location /health {
                return 200 "ok";
            }

        """ + ProxyLocation));

        Assert.Equal("gzip on;\nlocation /health {\n    return 200 \"ok\";\n}", spec.ExtraServerDirectives);
    }

    [Fact]
    public void Max_body_size_ipv6_and_http_port_are_read()
    {
        SiteSpec spec = ImportOk("server {\n    listen 8080;\n    listen [::]:8080;\n    server_name a.example.com;\n    client_max_body_size 100m;\n" + ProxyLocation + "}\n");

        Assert.Equal(8080, spec.HttpPort);
        Assert.True(spec.Ipv6);
        Assert.Equal("100m", spec.MaxBodySize);
    }

    [Fact]
    public void A_redirect_plus_https_pair_becomes_tls_with_force_https()
    {
        SiteSpec spec = ImportOk("""
            server {
                listen 80;
                server_name app.example.com;
                return 301 https://$host$request_uri;
            }

            server {
                listen 443 ssl http2;
                server_name app.example.com;
                ssl_certificate /etc/letsencrypt/live/app.example.com/fullchain.pem;
                ssl_certificate_key /etc/letsencrypt/live/app.example.com/privkey.pem;
                add_header Strict-Transport-Security "max-age=31536000" always;
                location / {
                    proxy_pass http://127.0.0.1:3000;
                }
            }
            """);

        Assert.True(spec.EnableTls);
        Assert.True(spec.ForceHttps);
        Assert.True(spec.Http2);
        Assert.True(spec.Hsts);
        Assert.Equal(443, spec.HttpsPort);
        Assert.Equal("/etc/letsencrypt/live/app.example.com/fullchain.pem", spec.CertificatePath);
        Assert.Equal("/etc/letsencrypt/live/app.example.com/privkey.pem", spec.CertificateKeyPath);
    }

    [Fact]
    public void The_redirect_may_live_in_a_location_and_the_acme_challenge_is_recognised()
    {
        SiteSpec spec = ImportOk("""
            server {
                listen 80;
                server_name app.example.com;
                location ^~ /.well-known/acme-challenge/ {
                    root /var/www/certbot;
                }
                location / {
                    return 301 https://$host$request_uri;
                }
            }

            server {
                listen 443 ssl;
                http2 on;
                server_name app.example.com;
                ssl_certificate /c.pem;
                ssl_certificate_key /k.pem;
                location / {
                    proxy_pass http://127.0.0.1:3000;
                }
            }
            """);

        Assert.True(spec.ServeAcmeChallenge);
        Assert.True(spec.ForceHttps);
        Assert.True(spec.Http2);
    }

    [Fact]
    public void A_whitelist_include_followed_by_deny_all_is_read()
    {
        SiteSpec spec = ImportOk(Wrap("    include /etc/nginx/whitelists/office.conf;\n    deny all;\n" + ProxyLocation));

        Assert.Equal(["office.conf"], spec.Whitelists);
        Assert.Null(spec.ExtraServerDirectives);
    }

    [Fact]
    public void An_include_without_deny_all_stays_a_free_directive()
    {
        SiteSpec spec = ImportOk(Wrap("    include /etc/nginx/whitelists/office.conf;\n" + ProxyLocation));

        Assert.Empty(spec.Whitelists);
        Assert.Contains("include /etc/nginx/whitelists/office.conf;", spec.ExtraServerDirectives);
    }

    [Fact]
    public void An_allow_next_to_a_whitelist_is_refused_because_the_order_would_change()
    {
        SiteImportResult result = Import(Wrap("    include /etc/nginx/whitelists/office.conf;\n    allow 1.2.3.4;\n    deny all;\n" + ProxyLocation));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Blockers, (string blocker) => blocker.Contains("allow", StringComparison.Ordinal));
    }

    private const string Whitelisted = "    include /etc/nginx/whitelists/office.conf;\n    deny all;\n";

    [Theory]
    [InlineData("return 404;", DeniedBehavior.NotFound, null)]
    [InlineData("return 444;", DeniedBehavior.CloseConnection, null)]
    [InlineData("return 302 https://example.com/?from=app;", DeniedBehavior.Redirect, "https://example.com/?from=app")]
    public void The_named_location_for_refused_visitors_is_read(string directive, DeniedBehavior behavior, string? url)
    {
        SiteSpec spec = ImportOk(Wrap(Whitelisted + "    error_page 403 = @rg_denied;\n    location @rg_denied {\n        allow all;\n        " + directive + "\n    }\n" + ProxyLocation));

        Assert.Equal(behavior, spec.DeniedBehavior);
        Assert.Equal(url, spec.DeniedUrl);
        Assert.Null(spec.ExtraServerDirectives);
    }

    [Fact]
    public void A_whitelist_alone_reads_as_forbidden()
    {
        SiteSpec spec = ImportOk(Wrap(Whitelisted + ProxyLocation));

        Assert.Equal(DeniedBehavior.Forbidden, spec.DeniedBehavior);
        Assert.Null(spec.DeniedUrl);
    }

    [Fact]
    public void The_named_location_without_a_whitelist_stays_a_free_directive()
    {
        SiteSpec spec = ImportOk(Wrap("    error_page 403 = @rg_denied;\n    location @rg_denied {\n        allow all;\n        return 404;\n    }\n" + ProxyLocation));

        Assert.Equal(DeniedBehavior.Forbidden, spec.DeniedBehavior);
        Assert.Contains("location @rg_denied", spec.ExtraServerDirectives);
    }

    [Theory]
    [InlineData("    error_page 403 = @rg_denied;\n", "does not exist")]
    [InlineData("    location @rg_denied { allow all; return 404; }\n", "never used")]
    [InlineData("    error_page 403 = @rg_denied;\n    location @rg_denied { return 404; }\n", "must only contain")]
    [InlineData("    error_page 403 = @rg_denied;\n    location @rg_denied { allow all; add_header X-A b; return 404; }\n", "must only contain")]
    [InlineData("    error_page 403 = @rg_denied;\n    location @rg_denied { allow all; return 301 https://example.com/; }\n", "only writes")]
    [InlineData("    error_page 403 = @rg_denied;\n    location @rg_denied { allow all; return 403; }\n", "only writes")]
    public void A_named_location_the_wizard_did_not_write_is_refused(string body, string expected)
    {
        SiteImportResult result = Import(Wrap(Whitelisted + body + ProxyLocation));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Blockers, (string blocker) => blocker.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Two_servers_that_refuse_differently_are_refused()
    {
        SiteImportResult result = Import("""
            server {
                listen 80;
                server_name app.example.com;
                include /etc/nginx/whitelists/office.conf;
                deny all;
                error_page 403 = @rg_denied;
                location @rg_denied { allow all; return 404; }
                return 301 https://$host$request_uri;
            }

            server {
                listen 443 ssl;
                server_name app.example.com;
                ssl_certificate /c.pem;
                ssl_certificate_key /k.pem;
                include /etc/nginx/whitelists/office.conf;
                deny all;
                error_page 403 = @rg_denied;
                location @rg_denied { allow all; return 444; }
                location / {
                    proxy_pass http://127.0.0.1:3000;
                }
            }
            """);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Blockers, (string blocker) => blocker.Contains("the same way", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DeniedBehavior.NotFound, null)]
    [InlineData(DeniedBehavior.CloseConnection, null)]
    [InlineData(DeniedBehavior.Redirect, "https://example.com/denied?from=app")]
    public void A_refusal_behavior_round_trips_through_the_generator_over_https(DeniedBehavior behavior, string? url)
    {
        SiteSpec original = new()
        {
            ServerNames = ["app.example.com"],
            Upstream = "http://127.0.0.1:3000",
            Websockets = false,
            EnableTls = true,
            CertificatePath = "/c.pem",
            CertificateKeyPath = "/k.pem",
            ServeAcmeChallenge = true,
            Whitelists = ["office.conf"],
            DeniedBehavior = behavior,
            DeniedUrl = url,
        };
        GenerationContext context = new(new ConfigLocations(Settings), Settings.AcmeWebroot, "app.example.com.conf", new Version(1, 27, 0));
        string generated = SiteGenerator.Generate(original, context);

        SiteSpec spec = ImportOk(generated);

        Assert.Equal(behavior, spec.DeniedBehavior);
        Assert.Equal(url, spec.DeniedUrl);
        Assert.Null(spec.ExtraServerDirectives);
        Assert.Equal(generated, SiteGenerator.Generate(spec, context));
    }

    [Fact]
    public void A_static_site_with_a_single_page_fallback_is_read()
    {
        SiteSpec spec = ImportOk(Wrap("""
            root /var/www/site;
            index index.html index.htm;
            location / {
                try_files $uri $uri/ /index.html;
            }

        """));

        Assert.Equal(SiteKind.StaticSite, spec.Kind);
        Assert.Equal("/var/www/site", spec.RootDirectory);
        Assert.True(spec.SpaFallback);
        Assert.False(spec.CacheStaticAssets);
        Assert.Null(spec.ExtraServerDirectives);
    }

    [Fact]
    public void Wizard_defaults_round_trip_through_the_generator()
    {
        SiteSpec original = new()
        {
            ServerNames = ["app.example.com", "www.example.com"],
            Upstream = "http://127.0.0.1:3000",
            EnableTls = true,
            CertificatePath = "/etc/letsencrypt/live/app.example.com/fullchain.pem",
            CertificateKeyPath = "/etc/letsencrypt/live/app.example.com/privkey.pem",
            Hsts = true,
            MaxBodySize = "50m",
            ProxyTimeoutSeconds = 120,
            ServeAcmeChallenge = true,
            Whitelists = ["office.conf"],
        };
        GenerationContext context = new(new ConfigLocations(Settings), Settings.AcmeWebroot, "app.example.com.conf", new Version(1, 27, 0));
        string generated = SiteGenerator.Generate(original, context);

        SiteImportResult result = Import(generated);

        // A top-level map is refused: that is the wizard's own websocket map, and the importer is for hand-written files.
        Assert.False(result.Succeeded);
        SiteSpec withoutWebsockets = original with { Websockets = false };
        SiteSpec spec = ImportOk(SiteGenerator.Generate(withoutWebsockets, context));
        Assert.Equal(["office.conf"], spec.Whitelists);
        Assert.Null(spec.ExtraServerDirectives);
        Assert.Null(spec.ExtraLocationDirectives);
        Assert.Equal(SiteGenerator.Generate(withoutWebsockets, context), SiteGenerator.Generate(spec, context));
    }

    [Fact]
    public void A_static_site_generated_by_the_wizard_round_trips()
    {
        SiteSpec original = new() { Kind = SiteKind.StaticSite, ServerNames = ["site.example.com"], RootDirectory = "/var/www/site", SpaFallback = true };
        GenerationContext context = new(new ConfigLocations(Settings), Settings.AcmeWebroot, "site.example.com", new Version(1, 27, 0));
        string generated = SiteGenerator.Generate(original, context);

        SiteSpec spec = ImportOk(generated);

        Assert.True(spec.SpaFallback);
        Assert.Equal(generated, SiteGenerator.Generate(spec, context));
    }

    [Fact]
    public void A_file_server_generated_by_the_wizard_round_trips()
    {
        SiteSpec original = new()
        {
            Kind = SiteKind.StaticSite,
            ServerNames = ["files.example.com"],
            RootDirectory = "/srv/files",
            BlockCommonExploits = true,
            FancyIndex = new FancyIndexOptions
            {
                DefaultSort = FancyIndexSort.DateDescending,
                ExactSize = false,
                LocalTime = true,
                Header = "/.nginxy/header.html",
                Footer = "/.nginxy/footer.html",
                Ignore = ["radarr", "uploads"],
                TimeFormat = "%d-%b-%Y %H:%M",
            },
        };
        
        GenerationContext context = new(
            new ConfigLocations(Settings),
            Settings.AcmeWebroot,
            "files.example.com",
            new Version(1, 27, 0)
        );
        
        string generated = SiteGenerator.Generate(original, context);

        SiteSpec spec = ImportOk(generated);

        FancyIndexOptions options = Assert.IsType<FancyIndexOptions>(spec.FancyIndex);
        
        Assert.Equal(FancyIndexSort.DateDescending, options.DefaultSort);
        Assert.Equal("/.nginxy/header.html", options.Header);
        Assert.Equal(["radarr", "uploads"], options.Ignore);
        Assert.Null(spec.ExtraServerDirectives);
        Assert.Null(spec.ExtraLocationDirectives);
        Assert.Equal(generated, SiteGenerator.Generate(spec, context));
    }

    private const string ExploitProtection = """
            if ($request_method ~* "^(TRACE|TRACK)$") { return 405; }
            location ~ /\.(?!well-known) { deny all; }

        """;

    private static string FileServer(string fancyIndexLines, string serverLines = "")
    {
        return Wrap($"    root /srv/files;\n{serverLines}    location / {{\n        try_files $uri $uri/ =404;\n        fancyindex on;\n{fancyIndexLines}    }}\n");
    }

    [Fact]
    public void Fancyindex_directives_are_read_into_the_options()
    {
        SiteSpec spec = ImportOk(FileServer("""
                    fancyindex_exact_size off;
                    fancyindex_localtime on;
                    fancyindex_default_sort size_desc;
                    fancyindex_time_format "%d-%b-%Y %H:%M";
                    fancyindex_header "/h.html";
                    fancyindex_ignore radarr uploads;

            """));

        FancyIndexOptions options = Assert.IsType<FancyIndexOptions>(spec.FancyIndex);
        Assert.False(options.ExactSize);
        Assert.True(options.LocalTime);
        Assert.Equal(FancyIndexSort.SizeDescending, options.DefaultSort);
        Assert.Equal("%d-%b-%Y %H:%M", options.TimeFormat);
        Assert.Equal("/h.html", options.Header);
        Assert.Equal(["radarr", "uploads"], options.Ignore);
        Assert.Null(spec.ExtraLocationDirectives);
    }

    [Fact]
    public void A_file_without_the_case_sensitivity_directive_is_not_reported_and_keeps_the_default()
    {
        SiteImportResult result = Import(FileServer("        fancyindex_exact_size off;\n"));

        Assert.True(result.Succeeded);
        Assert.True(result.Spec!.FancyIndex!.CaseSensitive);
        Assert.DoesNotContain(result.Notes, (string item) => item.Contains("fancyindex_case_sensitive", StringComparison.Ordinal));
    }

    [Fact]
    public void The_case_sensitivity_directive_is_read_when_it_is_off()
    {
        SiteSpec spec = ImportOk(FileServer("        fancyindex_case_sensitive off;\n"));

        Assert.False(spec.FancyIndex!.CaseSensitive);
    }

    [Fact]
    public void Missing_fancyindex_directives_are_reported_as_a_note()
    {
        SiteImportResult result = Import(FileServer("        fancyindex_exact_size off;\n"));

        Assert.True(result.Succeeded);
        string note = Assert.Single(result.Notes, (string item) => item.Contains("fancyindex_", StringComparison.Ordinal));
        Assert.Contains("fancyindex_localtime", note);
        Assert.Contains("fancyindex_default_sort", note);
        Assert.Contains("fancyindex_time_format", note);
        Assert.DoesNotContain("fancyindex_exact_size", note);
    }

    [Fact]
    public void A_file_with_every_fancyindex_directive_produces_no_note()
    {
        SiteImportResult result = Import(FileServer("""
                    fancyindex_exact_size on;
                    fancyindex_localtime off;
                    fancyindex_show_path on;
                    fancyindex_directories_first on;
                    fancyindex_case_sensitive on;
                    fancyindex_show_dotfiles off;
                    fancyindex_hide_symlinks off;
                    fancyindex_hide_parent_dir off;
                    fancyindex_default_sort name;
                    fancyindex_time_format "%Y-%b-%d %H:%M";

            """));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(result.Notes, (string item) => item.Contains("fancyindex_", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_fancyindex_directive_stays_a_free_directive()
    {
        SiteSpec spec = ImportOk(FileServer("        fancyindex_name_length 255;\n"));

        Assert.NotNull(spec.FancyIndex);
        Assert.Equal("fancyindex_name_length 255;", spec.ExtraLocationDirectives);
    }

    [Fact]
    public void Fancyindex_directives_without_fancyindex_on_are_left_alone()
    {
        SiteImportResult result = Import(Wrap("    root /srv/files;\n    location / {\n        try_files $uri $uri/ =404;\n        fancyindex_exact_size off;\n    }\n"));

        Assert.True(result.Succeeded);
        Assert.Null(result.Spec!.FancyIndex);
        Assert.Equal("fancyindex_exact_size off;", result.Spec.ExtraLocationDirectives);
    }

    [Theory]
    [InlineData("fancyindex_default_sort foo;")]
    [InlineData("fancyindex_default_sort;")]
    [InlineData("fancyindex_default_sort name size;")]
    [InlineData("fancyindex_exact_size maybe;")]
    [InlineData("fancyindex_header /h.html local;")]
    [InlineData("fancyindex_time_format;")]
    public void A_fancyindex_value_the_wizard_cannot_write_is_refused(string line)
    {
        SiteImportResult result = Import(FileServer($"        {line}\n"));

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Blockers);
    }

    [Fact]
    public void A_header_without_a_directory_does_not_crash_the_import()
    {
        SiteSpec spec = ImportOk(FileServer("        fancyindex_header \"header.html\";\n", ExploitProtection));

        Assert.Equal("header.html", spec.FancyIndex!.Header);
        Assert.True(spec.BlockCommonExploits);
    }

    [Fact]
    public void The_asset_location_the_wizard_writes_is_not_kept_twice()
    {
        SiteSpec spec = ImportOk(FileServer(
            "        fancyindex_header \"/.nginxy/header.html\";\n",
            ExploitProtection + "    location ^~ /.nginxy/ { try_files $uri =404; }\n"));

        Assert.Equal("/.nginxy/header.html", spec.FancyIndex!.Header);
        Assert.Null(spec.ExtraServerDirectives);
    }

    [Fact]
    public void An_asset_location_the_wizard_would_not_write_stays_a_free_directive()
    {
        SiteSpec spec = ImportOk(FileServer(
            "        fancyindex_header \"/.nginxy/header.html\";\n",
            ExploitProtection + "    location ^~ /.nginxy/ { return 403; }\n"));

        Assert.Contains("return 403;", spec.ExtraServerDirectives);
    }

    [Theory]
    [InlineData("upstream backend { server 127.0.0.1:3000; }\n")]
    [InlineData("map $a $b { default 1; }\n")]
    public void Top_level_statements_other_than_servers_are_refused(string prefix)
    {
        SiteImportResult result = Import(prefix + Wrap(ProxyLocation));

        Assert.False(result.Succeeded);
        Assert.Contains("outside a server block", result.Blockers[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server { listen 127.0.0.1:80; server_name a.example.com; location / { proxy_pass http://a:1; } }", "specific address")]
    [InlineData("server { listen 80 default_server; server_name a.example.com; location / { proxy_pass http://a:1; } }", "default_server")]
    [InlineData("server { listen 80; server_name ~^www\\d+\\.example\\.com$; location / { proxy_pass http://a:1; } }", "pattern")]
    [InlineData("server { listen 80; server_name a.example.com; location / { proxy_pass http://a:1; proxy_set_header Host $http_host; } }", "differs")]
    [InlineData("server { listen 80; server_name a.example.com; location / { proxy_pass http://a:1; proxy_http_version 1.0; } }", "1.1")]
    [InlineData("server { listen 80; server_name a.example.com; location / { proxy_pass http://a:1; proxy_set_header Connection close; } }", "Connection")]
    [InlineData("server { listen 80; server_name a.example.com; gzip on; }", "location /")]
    [InlineData("server { listen 80; server_name a.example.com; root /var/www; location / { try_files $uri =404; } }", "try_files")]
    [InlineData("server { listen 80; server_name a.example.com; root /var/www; index home.html; location / { try_files $uri $uri/ =404; } }", "index files")]
    public void Files_the_wizard_could_not_reproduce_are_refused_with_a_reason(string content, string expected)
    {
        SiteImportResult result = Import(content);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Blockers, (string blocker) => blocker.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Three_server_blocks_are_refused()
    {
        string server = "server { listen 80; server_name a.example.com; location / { proxy_pass http://a:1; } }\n";

        SiteImportResult result = Import(server + server + server);

        Assert.False(result.Succeeded);
        Assert.Contains("more than two", result.Blockers[0], StringComparison.Ordinal);
    }

    [Fact]
    public void An_https_only_file_is_refused_because_an_http_server_would_be_added()
    {
        SiteImportResult result = Import("server { listen 443 ssl; server_name a.example.com; ssl_certificate /c.pem; ssl_certificate_key /k.pem; location / { proxy_pass http://a:1; } }");

        Assert.False(result.Succeeded);
        Assert.Contains("only has an HTTPS server", result.Blockers[0], StringComparison.Ordinal);
    }

    [Fact]
    public void An_http_server_that_does_more_than_redirect_is_refused()
    {
        SiteImportResult result = Import("""
            server { listen 80; server_name a.example.com; location / { proxy_pass http://a:1; } }
            server { listen 443 ssl; server_name a.example.com; ssl_certificate /c.pem; ssl_certificate_key /k.pem; location / { proxy_pass http://a:1; } }
            """);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Blockers, (string blocker) => blocker.Contains("return 301", StringComparison.Ordinal));
    }

    [Fact]
    public void Different_ssl_protocols_are_refused_because_nginx_rejects_the_directive_twice()
    {
        SiteImportResult result = Import("""
            server { listen 80; server_name a.example.com; return 301 https://$host$request_uri; }
            server { listen 443 ssl; server_name a.example.com; ssl_certificate /c.pem; ssl_certificate_key /k.pem; ssl_protocols TLSv1.3; location / { proxy_pass http://a:1; } }
            """);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Blockers, (string blocker) => blocker.Contains("ssl_protocols", StringComparison.Ordinal));
    }

    [Fact]
    public void Text_that_is_not_nginx_is_refused_with_the_parse_error()
    {
        SiteImportResult result = Import("server {\n    listen 80\n}\n");

        Assert.False(result.Succeeded);
        Assert.Contains("line", result.Blockers[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Comments_outside_the_extras_produce_a_note()
    {
        SiteImportResult result = Import("# my site\n" + Wrap(ProxyLocation));

        Assert.Contains(result.Notes, (string note) => note.Contains("Comments", StringComparison.Ordinal));
    }
}
