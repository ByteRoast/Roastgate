using Roastgate.Core.Nginx.Generation;

namespace Roastgate.Tests.Generation;

public sealed class SiteSpecValidatorTests
{
    private static SiteSpec Proxy()
    {
        return new SiteSpec { ServerNames = ["app.example.com"], Upstream = "http://127.0.0.1:3000" };
    }

    private static bool HasProblem(SiteSpec spec, string field)
    {
        return SiteSpecValidator.Validate(spec).Any((SpecProblem problem) => problem.Field == field);
    }

    [Fact]
    public void A_minimal_reverse_proxy_is_valid()
    {
        Assert.Empty(SiteSpecValidator.Validate(Proxy()));
    }

    [Fact]
    public void A_minimal_static_site_is_valid()
    {
        SiteSpec spec = new()
            { Kind = SiteKind.StaticSite, ServerNames = ["docs.example.com"], RootDirectory = "/var/www/docs" };

        Assert.Empty(SiteSpecValidator.Validate(spec));
    }

    [Fact]
    public void A_fully_loaded_https_proxy_is_valid()
    {
        SiteSpec spec = Proxy() with
        {
            ServerNames = ["app.example.com", "*.example.com", "_"],
            EnableTls = true,
            CertificatePath = "/etc/letsencrypt/live/app/fullchain.pem",
            CertificateKeyPath = "/etc/letsencrypt/live/app/privkey.pem",
            Hsts = true,
            MaxBodySize = "100m",
            ProxyTimeoutSeconds = 300,
            Whitelists = ["alice.conf", "bob.conf"],
            ExtraServerDirectives = "location /health { return 200; }",
            ExtraLocationDirectives = "proxy_buffering off;",
        };

        Assert.Empty(SiteSpecValidator.Validate(spec));
    }

    // ---- Server names ----------------------------------------------------------------

    [Theory]
    [InlineData("example.com")]
    [InlineData("a.b.example.com")]
    [InlineData("xn--bcher-kva.example")]
    [InlineData("*.example.com")]
    [InlineData("_")]
    [InlineData("localhost")]
    [InlineData("my-app.example.com")]
    public void Valid_server_names_are_accepted(string name)
    {
        Assert.False(HasProblem(Proxy() with { ServerNames = [name] }, nameof(SiteSpec.ServerNames)), name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("exa mple.com")]
    [InlineData("example.com;")]
    [InlineData("example.com }")]
    [InlineData("example.com\nserver_name evil.com")]
    [InlineData("a..b.com")]
    [InlineData("-a.com")]
    [InlineData("a-.com")]
    [InlineData("exa$mple.com")]
    [InlineData("example.com/path")]
    [InlineData("ex\"ample.com")]
    [InlineData("bücher.example")]
    [InlineData("a*.example.com")]
    public void Anything_that_is_not_a_plain_hostname_is_refused(string name)
    {
        Assert.True(HasProblem(Proxy() with { ServerNames = [name] }, nameof(SiteSpec.ServerNames)), name);
    }

    [Fact]
    public void At_least_one_server_name_is_required()
    {
        Assert.True(HasProblem(Proxy() with { ServerNames = [] }, nameof(SiteSpec.ServerNames)));
    }

    [Fact]
    public void Too_many_server_names_are_refused()
    {
        string[] names = Enumerable.Range(0, SiteSpecValidator.MaximumServerNames + 1)
            .Select((int index) => $"h{index}.example.com").ToArray();

        Assert.True(HasProblem(Proxy() with { ServerNames = names }, nameof(SiteSpec.ServerNames)));
    }

    [Fact]
    public void A_server_name_listed_twice_is_refused_whatever_its_case()
    {
        Assert.True(HasProblem(Proxy() with { ServerNames = ["a.example.com", "A.EXAMPLE.COM"] },
            nameof(SiteSpec.ServerNames)));
    }

    // ---- Upstream --------------------------------------------------------------------

    [Theory]
    [InlineData("http://127.0.0.1:3000")]
    [InlineData("https://backend.internal:8443")]
    [InlineData("http://localhost")]
    [InlineData("http://[::1]:8080")]
    [InlineData("http://my_app:3000")]
    [InlineData("http://backend:3000/")]
    [InlineData("http://backend:3000/app/")]
    [InlineData("https://backend/api/v1/")]
    public void Valid_destinations_are_accepted(string upstream)
    {
        Assert.False(HasProblem(Proxy() with { Upstream = upstream }, nameof(SiteSpec.Upstream)), upstream);
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1:3000")]
    [InlineData("ftp://backend")]
    [InlineData("http://")]
    [InlineData("http://back end")]
    [InlineData("http://backend;")]
    [InlineData("http://backend:3000; deny all")]
    [InlineData("http://backend/\"")]
    [InlineData("http://backend:99999")]
    [InlineData("http://backend:0")]
    [InlineData("http://backend/../etc/")]
    [InlineData("http://backend/$uri")]
    [InlineData("http://unix:/tmp/app.sock:/")]
    [InlineData("http://backend:3000\nlocation / {")]
    public void Destinations_that_could_change_the_meaning_of_the_directive_are_refused(string upstream)
    {
        Assert.True(HasProblem(Proxy() with { Upstream = upstream }, nameof(SiteSpec.Upstream)), upstream);
    }

    [Fact]
    public void A_destination_path_without_a_trailing_slash_is_refused_because_nginx_would_glue_it_to_the_request()
    {
        SpecProblem problem = Assert.Single(
            SiteSpecValidator.Validate(Proxy() with { Upstream = "http://backend:3000/app" }),
            (SpecProblem item) => item.Field == nameof(SiteSpec.Upstream));

        Assert.Contains("slash", problem.Message);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(60, true)]
    [InlineData(3600, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(3601, false)]
    public void The_proxy_timeout_is_bounded(int seconds, bool valid)
    {
        Assert.Equal(valid,
            !HasProblem(Proxy() with { ProxyTimeoutSeconds = seconds }, nameof(SiteSpec.ProxyTimeoutSeconds)));
    }

    // ---- Static site -----------------------------------------------------------------

    [Theory]
    [InlineData("/var/www/site", true)]
    [InlineData("/srv/my-site_2/public", true)]
    [InlineData("var/www", false)]
    [InlineData("/var/www/../etc", false)]
    [InlineData("/var/www/my site", false)]
    [InlineData("/var/www;", false)]
    [InlineData("", false)]
    [InlineData("/", false)]
    public void The_root_directory_is_a_safe_absolute_path(string root, bool valid)
    {
        SiteSpec spec = new() { Kind = SiteKind.StaticSite, ServerNames = ["a.example.com"], RootDirectory = root };

        Assert.Equal(valid, !HasProblem(spec, nameof(SiteSpec.RootDirectory)));
    }

    // ---- HTTPS -----------------------------------------------------------------------

    [Fact]
    public void Https_needs_a_certificate_and_a_key()
    {
        SiteSpec spec = Proxy() with { EnableTls = true };

        Assert.True(HasProblem(spec, nameof(SiteSpec.CertificatePath)));
        Assert.True(HasProblem(spec, nameof(SiteSpec.CertificateKeyPath)));
    }

    [Theory]
    [InlineData("/etc/ssl/cert.pem; ssl_verify_client off")]
    [InlineData("etc/ssl/cert.pem")]
    [InlineData("/etc/ssl/../../etc/passwd")]
    [InlineData("/etc/ssl/my cert.pem")]
    public void Certificate_paths_cannot_smuggle_directives(string path)
    {
        SiteSpec spec = Proxy() with
        {
            EnableTls = true, CertificatePath = path, CertificateKeyPath = "/etc/ssl/key.pem"
        };

        Assert.True(HasProblem(spec, nameof(SiteSpec.CertificatePath)), path);
    }

    [Fact]
    public void The_http_and_https_ports_must_differ()
    {
        SiteSpec spec = Proxy() with
        {
            EnableTls = true, CertificatePath = "/c.pem", CertificateKeyPath = "/k.pem", HttpPort = 8080,
            HttpsPort = 8080
        };

        Assert.True(HasProblem(spec, nameof(SiteSpec.HttpsPort)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void Ports_are_bounded(int port)
    {
        Assert.True(HasProblem(Proxy() with { HttpPort = port }, nameof(SiteSpec.HttpPort)));
    }

    [Fact]
    public void Hsts_without_https_is_refused()
    {
        Assert.True(HasProblem(Proxy() with { EnableTls = false, Hsts = true }, nameof(SiteSpec.Hsts)));
    }

    // ---- Limits and whitelists -------------------------------------------------------

    [Theory]
    [InlineData("100m", true)]
    [InlineData("512k", true)]
    [InlineData("2G", true)]
    [InlineData("0", true)]
    [InlineData("1", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("100 m", false)]
    [InlineData("-1", false)]
    [InlineData("1.5m", false)]
    [InlineData("abc", false)]
    [InlineData("00", false)]
    [InlineData("100m;", false)]
    public void The_upload_limit_is_a_size(string? size, bool valid)
    {
        Assert.Equal(valid, !HasProblem(Proxy() with { MaxBodySize = size }, nameof(SiteSpec.MaxBodySize)));
    }

    [Theory]
    [InlineData("alice.conf", true)]
    [InlineData("alice", false)]
    [InlineData("../alice.conf", false)]
    [InlineData("a/b.conf", false)]
    [InlineData("alice.conf;", false)]
    public void Whitelists_are_referenced_by_their_normalised_file_name(string name, bool valid)
    {
        Assert.Equal(valid, !HasProblem(Proxy() with { Whitelists = [name] }, nameof(SiteSpec.Whitelists)));
    }

    // ---- Free directives -------------------------------------------------------------

    [Theory]
    [InlineData("proxy_buffering off;")]
    [InlineData("location /a { return 200; }")]
    [InlineData("location /a { if ($x) { return 200; } }")]
    [InlineData("add_header X-Note \"a } in a string\";")]
    [InlineData("# a } in a comment\nproxy_buffering off;")]
    [InlineData("add_header X-Note 'it''s { fine';")]
    public void Free_directives_with_balanced_braces_are_accepted(string text)
    {
        Assert.False(HasProblem(Proxy() with { ExtraServerDirectives = text }, nameof(SiteSpec.ExtraServerDirectives)),
            text);

        Assert.False(
            HasProblem(Proxy() with { ExtraLocationDirectives = text }, nameof(SiteSpec.ExtraLocationDirectives)),
            text);
    }

    [Theory]
    [InlineData("}")]
    [InlineData("proxy_buffering off; } server { listen 9999;")]
    [InlineData("location /a {")]
    [InlineData("} }")]
    [InlineData("add_header X \"unterminated;")]
    [InlineData("a }\n{ b")]
    public void Free_directives_that_could_leave_their_block_are_refused(string text)
    {
        Assert.True(HasProblem(Proxy() with { ExtraServerDirectives = text }, nameof(SiteSpec.ExtraServerDirectives)),
            text);

        Assert.True(
            HasProblem(Proxy() with { ExtraLocationDirectives = text }, nameof(SiteSpec.ExtraLocationDirectives)),
            text);
    }

    [Fact]
    public void Free_directives_cannot_contain_control_characters_or_be_huge()
    {
        Assert.True(HasProblem(Proxy() with { ExtraServerDirectives = "a;\0b;" },
            nameof(SiteSpec.ExtraServerDirectives)));

        Assert.True(HasProblem(
            Proxy() with { ExtraServerDirectives = new string('a', SiteSpecValidator.MaximumExtraLength + 1) },
            nameof(SiteSpec.ExtraServerDirectives)));
    }

    // ---- Denied visitors --------------------------------------------------------------

    [Theory]
    [InlineData(DeniedBehavior.NotFound)]
    [InlineData(DeniedBehavior.CloseConnection)]
    public void A_denied_behavior_with_a_whitelist_is_valid(DeniedBehavior behavior)
    {
        Assert.Empty(
            SiteSpecValidator.Validate(Proxy() with { Whitelists = ["alice.conf"], DeniedBehavior = behavior }));
    }

    [Theory]
    [InlineData(DeniedBehavior.NotFound)]
    [InlineData(DeniedBehavior.CloseConnection)]
    [InlineData(DeniedBehavior.Redirect)]
    public void A_denied_behavior_without_a_whitelist_is_refused(DeniedBehavior behavior)
    {
        SiteSpec spec = Proxy() with { DeniedBehavior = behavior, DeniedUrl = "https://example.com/" };

        Assert.True(HasProblem(spec, nameof(SiteSpec.DeniedBehavior)));
    }

    [Fact]
    public void Forbidden_needs_nothing_more_even_without_a_whitelist()
    {
        Assert.Empty(SiteSpecValidator.Validate(Proxy() with
        {
            DeniedBehavior = DeniedBehavior.Forbidden, DeniedUrl = "not a url"
        }));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com:8443/denied.html")]
    [InlineData("https://example.com/?from=app&reason=ip")]
    [InlineData("https://[2001:db8::1]/")]
    public void Valid_redirect_addresses_are_accepted(string url)
    {
        SiteSpec spec = Proxy() with
        {
            Whitelists = ["alice.conf"], DeniedBehavior = DeniedBehavior.Redirect, DeniedUrl = url
        };

        Assert.Empty(SiteSpecValidator.Validate(spec));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.com")]
    [InlineData("ftp://example.com/")]
    [InlineData("https://example.com/ a")]
    [InlineData("https://example.com/; return 200")]
    [InlineData("https://example.com/$request_uri")]
    [InlineData("https://example.com/{a}")]
    [InlineData("https://example.com/\"a\"")]
    [InlineData("https://example.com/#top")]
    [InlineData("https://example.com:0/")]
    [InlineData("https://example.com:70000/")]
    public void Invalid_redirect_addresses_are_refused_on_the_url_field(string? url)
    {
        SiteSpec spec = Proxy() with
        {
            Whitelists = ["alice.conf"], DeniedBehavior = DeniedBehavior.Redirect, DeniedUrl = url
        };

        Assert.True(HasProblem(spec, nameof(SiteSpec.DeniedUrl)), url);
    }

    [Fact]
    public void A_redirect_address_cannot_be_huge()
    {
        string url = "https://example.com/" + new string('a', SiteSpecValidator.MaximumRedirectUrlLength);
        SiteSpec spec = Proxy() with
        {
            Whitelists = ["alice.conf"], DeniedBehavior = DeniedBehavior.Redirect, DeniedUrl = url
        };

        Assert.True(HasProblem(spec, nameof(SiteSpec.DeniedUrl)));
    }

    [Fact]
    public void The_redirect_address_is_ignored_by_the_other_behaviors()
    {
        SiteSpec spec = Proxy() with
        {
            Whitelists = ["alice.conf"], DeniedBehavior = DeniedBehavior.NotFound, DeniedUrl = "not a url"
        };

        Assert.Empty(SiteSpecValidator.Validate(spec));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("{ }", true)]
    [InlineData("{ { } }", true)]
    [InlineData("}{", false)]
    [InlineData("{", false)]
    [InlineData("# { only in a comment", true)]
    [InlineData("\"{\"", true)]
    [InlineData("\"{", false)]
    public void Brace_balance_ignores_strings_and_comments(string text, bool balanced)
    {
        Assert.Equal(balanced, SiteSpecValidator.BracesBalanced(text));
    }
}
