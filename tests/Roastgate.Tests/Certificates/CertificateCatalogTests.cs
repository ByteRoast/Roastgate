using Roastgate.Core.Certificates;
using Roastgate.Core.Nginx;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Certificates;

public sealed class CertificateCatalogTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static ICertificateCatalog Catalog(NginxTestbed nginx)
    {
        return nginx.Database.Get<ICertificateCatalog>();
    }

    // ---- Reading one certificate -----------------------------------------------------

    [Fact]
    public void A_certificate_is_read_with_its_names_validity_and_issuer()
    {
        string pem = TestCertificates.CreatePem("app.example.com", ["app.example.com", "www.example.com"], Now.AddDays(-30), Now.AddDays(60), "R11");

        CertificateInfo? info = CertificateCatalog.Parse("app.example.com", "/live/app/fullchain.pem", "/live/app/privkey.pem", pem);

        Assert.NotNull(info);
        Assert.Equal(["app.example.com", "www.example.com"], info.Domains);
        Assert.Equal("R11", info.Issuer);
        Assert.False(info.IsStaging);
        Assert.InRange(info.DaysLeft(Now), 58, 60);
        Assert.Equal("/live/app/fullchain.pem", info.FullChainPath);
    }

    [Fact]
    public void The_first_certificate_of_a_fullchain_is_the_one_that_is_read()
    {
        string pem = TestCertificates.CreatePem("leaf.example.com", ["leaf.example.com"], Now.AddDays(-1), Now.AddDays(30), "Some Intermediate");

        Assert.Equal(2, pem.Split("BEGIN CERTIFICATE").Length - 1);
        Assert.Equal(["leaf.example.com"], CertificateCatalog.Parse("leaf", "/f", "/k", pem)!.Domains);
    }

    [Theory]
    [InlineData("(STAGING) Artificial Apricot R3")]
    [InlineData("Fake LE Intermediate X1")]
    public void Certificates_from_the_staging_server_are_recognised(string issuer)
    {
        string pem = TestCertificates.CreatePem("a.example.com", ["a.example.com"], Now.AddDays(-1), Now.AddDays(30), issuer);

        Assert.True(CertificateCatalog.Parse("a", "/f", "/k", pem)!.IsStaging);
    }

    [Fact]
    public void Without_alternative_names_the_common_name_is_used()
    {
        string pem = TestCertificates.CreatePem("old.example.com", [], Now.AddDays(-1), Now.AddDays(30));

        Assert.Equal(["old.example.com"], CertificateCatalog.Parse("old", "/f", "/k", pem)!.Domains);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a certificate")]
    [InlineData("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----\n")]
    public void Anything_that_is_not_a_certificate_is_reported_as_unreadable_instead_of_throwing(string pem)
    {
        Assert.Null(CertificateCatalog.Parse("broken", "/f", "/k", pem));
    }

    [Theory]
    [InlineData("example.com", "example.com", true)]
    [InlineData("example.com", "EXAMPLE.COM", true)]
    [InlineData("*.example.com", "app.example.com", true)]
    [InlineData("*.example.com", "example.com", false)]
    [InlineData("*.example.com", "a.b.example.com", false)]
    [InlineData("*.example.com", "evilexample.com", false)]
    [InlineData("example.com", "app.example.com", false)]
    public void A_certificate_covers_exact_names_and_single_level_wildcards(string domain, string host, bool covered)
    {
        CertificateInfo info = new("c", [domain], "i", Now, Now.AddDays(1), false, "/f", "/k");

        Assert.Equal(covered, info.Covers(host));
    }

    // ---- The live directory ----------------------------------------------------------

    [Fact]
    public async Task Certificates_are_listed_soonest_to_expire_first()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        TestCertificates.WriteLive(nginx.LetsEncrypt, "later.example.com", ["later.example.com"], Now.AddDays(80));
        TestCertificates.WriteLive(nginx.LetsEncrypt, "sooner.example.com", ["sooner.example.com"], Now.AddDays(10));

        CertificateListing listing = await Catalog(nginx).ListAsync();

        Assert.True(listing.DirectoryExists);
        Assert.Equal(["sooner.example.com", "later.example.com"], listing.Certificates.Select((CertificateEntry entry) => entry.Certificate.Name).ToArray());
    }

    [Fact]
    public async Task A_missing_live_directory_is_reported_not_an_error()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        CertificateListing listing = await Catalog(nginx).ListAsync();

        Assert.False(listing.DirectoryExists);
        Assert.Empty(listing.Certificates);
    }

    [Fact]
    public async Task Directories_without_a_fullchain_are_ignored_and_broken_ones_are_named()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        TestCertificates.WriteLive(nginx.LetsEncrypt, "good.example.com", ["good.example.com"], Now.AddDays(30));
        Directory.CreateDirectory(Path.Combine(nginx.LetsEncrypt, "live", "empty"));
        Directory.CreateDirectory(Path.Combine(nginx.LetsEncrypt, "live", "broken"));
        await File.WriteAllTextAsync(Path.Combine(nginx.LetsEncrypt, "live", "broken", "fullchain.pem"), "garbage");
        await File.WriteAllTextAsync(Path.Combine(nginx.LetsEncrypt, "live", "README"), "certbot readme");

        CertificateListing listing = await Catalog(nginx).ListAsync();

        Assert.Equal("good.example.com", Assert.Single(listing.Certificates).Certificate.Name);
        Assert.Equal(["broken"], listing.Unreadable);
    }

    // ---- Where certificates are used -------------------------------------------------

    [Fact]
    public async Task The_configs_that_use_a_certificate_are_listed_with_their_line()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        string fullChain = TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(30)).Replace('\\', '/');
        string key = fullChain.Replace("fullchain.pem", "privkey.pem");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), $"server {{\n    ssl_certificate {fullChain};\n    ssl_certificate_key {key};\n}}\n");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "other.conf"), "server {\n    ssl_certificate /etc/ssl/other.pem;\n}\n");

        CertificateListing listing = await Catalog(nginx).ListAsync();

        CertificateUsage usage = Assert.Single(listing.Certificates.Single().Usage);
        Assert.Equal("app.conf", usage.FileName);
        Assert.Equal(2, usage.Line);
        Assert.True(usage.FileEnabled);
    }

    [Fact]
    public async Task A_disabled_proxy_that_uses_a_certificate_still_counts_but_is_flagged_inactive()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        string fullChain = TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(30)).Replace('\\', '/');
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "old.conf.disabled"), $"ssl_certificate {fullChain};\n");

        CertificateUsage usage = Assert.Single((await Catalog(nginx).ListAsync()).Certificates.Single().Usage);

        Assert.Equal("old.conf", usage.FileName);
        Assert.False(usage.FileEnabled);
    }

    [Fact]
    public async Task A_commented_out_directive_and_the_key_directive_do_not_count()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        string fullChain = TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(30)).Replace('\\', '/');
        string key = fullChain.Replace("fullchain.pem", "privkey.pem");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), $"# ssl_certificate {fullChain};\n    ssl_certificate_key {key};\n");

        Assert.Empty((await Catalog(nginx).ListAsync()).Certificates.Single().Usage);
    }

    [Fact]
    public async Task A_certificate_in_another_live_directory_is_not_confused_with_this_one()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        TestCertificates.WriteLive(nginx.LetsEncrypt, "a.example.com", ["a.example.com"], Now.AddDays(30));
        string other = TestCertificates.WriteLive(nginx.LetsEncrypt, "a.example.com-0001", ["b.example.com"], Now.AddDays(30)).Replace('\\', '/');
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "b.conf"), $"ssl_certificate {other};\n");

        CertificateListing listing = await Catalog(nginx).ListAsync();

        Assert.Empty(listing.Certificates.Single((CertificateEntry entry) => entry.Certificate.Name == "a.example.com").Usage);
        Assert.Single(listing.Certificates.Single((CertificateEntry entry) => entry.Certificate.Name == "a.example.com-0001").Usage);
    }
}
