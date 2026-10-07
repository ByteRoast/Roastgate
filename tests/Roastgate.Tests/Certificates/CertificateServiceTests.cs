using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core;
using Roastgate.Core.Certificates;
using Roastgate.Core.Data;
using Roastgate.Core.Hosting;
using Roastgate.Core.Settings;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Certificates;

public sealed class CertificateServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static HostCommandResult Ok(string output = "Successfully received certificate.")
    {
        return new HostCommandResult(0, output, string.Empty, false);
    }

    private static HostCommandResult Failed(string output)
    {
        return new HostCommandResult(1, string.Empty, output, false);
    }

    private sealed record Setup(NginxTestbed Nginx, ScriptedLocalExec Local, RecordingHostExec Host, ICertificateService Service);

    private static async Task<Setup> CreateAsync(
        CertbotMode mode = CertbotMode.Embedded,
        Func<string, IReadOnlyList<string>, HostCommandResult>? script = null,
        bool createWebroot = true)
    {
        ScriptedLocalExec local = new(script ?? ((string _, IReadOnlyList<string> _) => Ok()));
        RecordingHostExec host = new(script ?? ((string _, IReadOnlyList<string> _) => Ok()));
        NginxTestbed nginx = await NginxTestbed.CreateAsync(
            configureServices: (IServiceCollection services) =>
            {
                services.AddSingleton<ILocalExec>(local);
                services.AddSingleton<IHostExec>(host);
            },
            configureSettings: (AppSettings settings) => settings with { CertbotMode = mode });
        if (createWebroot)
        {
            Directory.CreateDirectory(Path.Combine(nginx.Root, "acme"));
        }

        return new Setup(nginx, local, host, nginx.Database.Get<ICertificateService>());
    }

    private static IssueRequest Request(params string[] domains)
    {
        return new IssueRequest(domains, null, "ops@example.com", false, false);
    }

    // ---- Issuing ---------------------------------------------------------------------

    [Fact]
    public async Task The_embedded_certbot_runs_in_the_container_with_the_shared_directories()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.IssueAsync(Request("app.example.com", "www.example.com"), "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(setup.Host.Calls);
        (string program, string[] arguments) = Assert.Single(setup.Local.Calls);
        Assert.Equal("certbot", program);
        Assert.Equal(
            [
                "certonly", "--webroot", "-w", Path.Combine(nginx.Root, "acme"),
                "--cert-name", "app.example.com",
                "--non-interactive", "--agree-tos", "--no-eff-email", "--expand",
                "--preferred-challenges", "http",
                "-d", "app.example.com", "-d", "www.example.com",
                "--email", "ops@example.com",
                "--config-dir", nginx.LetsEncrypt,
                "--work-dir", Path.Combine(nginx.Database.Get<RoastgateOptions>().DataDirectory, "certbot", "work"),
                "--logs-dir", Path.Combine(nginx.Database.Get<RoastgateOptions>().DataDirectory, "certbot", "logs"),
            ],
            arguments);
    }

    [Fact]
    public async Task The_host_certbot_runs_on_the_host_and_is_left_to_use_its_own_directories()
    {
        Setup setup = await CreateAsync(CertbotMode.Host);
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.IssueAsync(Request("app.example.com"), "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(setup.Local.Calls);
        (string program, string[] arguments) = Assert.Single(setup.Host.Calls);
        Assert.Equal("certbot", program);
        Assert.DoesNotContain("--config-dir", arguments);
        Assert.DoesNotContain("--work-dir", arguments);
        Assert.Contains("certonly", arguments);
    }

    [Fact]
    public async Task Staging_dry_run_a_custom_name_and_no_email_change_the_arguments()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.IssueAsync(new IssueRequest(["App.Example.com"], "my-cert", null, Staging: true, DryRun: true), "admin");

        Assert.True(result.Succeeded);
        Assert.Contains("dry run", result.Message);
        string[] arguments = setup.Local.Calls.Single().Arguments;
        Assert.Contains("--staging", arguments);
        Assert.Contains("--dry-run", arguments);
        Assert.Contains("--register-unsafely-without-email", arguments);
        Assert.DoesNotContain("--email", arguments);
        Assert.Equal("my-cert", arguments[Array.IndexOf(arguments, "--cert-name") + 1]);
        Assert.Equal("app.example.com", arguments[Array.IndexOf(arguments, "-d") + 1]);
    }

    [Theory]
    [InlineData("a.example.com; rm -rf /")]
    [InlineData("a.example.com --post-hook evil")]
    [InlineData("-d")]
    [InlineData("--staging")]
    [InlineData("localhost")]
    [InlineData("*.example.com")]
    [InlineData("exa mple.com")]
    [InlineData("a..example.com")]
    [InlineData("")]
    public async Task A_domain_that_could_be_taken_for_an_option_or_is_not_a_public_name_never_reaches_certbot(string domain)
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.IssueAsync(Request("ok.example.com", domain), "admin");

        Assert.False(result.Succeeded);
        Assert.Empty(setup.Local.Calls);
    }

    [Fact]
    public async Task Wildcard_domains_get_an_explanation()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.IssueAsync(Request("*.example.com"), "admin");

        Assert.Contains("DNS", result.Message);
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("--staging")]
    [InlineData("UPPER")]
    [InlineData("../x")]
    public async Task A_certificate_name_cannot_be_an_option_or_a_path(string name)
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.IssueAsync(new IssueRequest(["a.example.com"], name, null, false, false), "admin");

        Assert.False(result.Succeeded);
        Assert.Empty(setup.Local.Calls);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a b@example.com")]
    public async Task An_invalid_email_is_refused(string email)
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.IssueAsync(new IssueRequest(["a.example.com"], null, email, false, false), "admin");

        Assert.False(result.Succeeded);
        Assert.Empty(setup.Local.Calls);
    }

    [Fact]
    public async Task Duplicate_domains_are_refused()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        Assert.False((await setup.Service.IssueAsync(Request("a.example.com", "A.example.com"), "admin")).Succeeded);
    }

    [Fact]
    public async Task Too_many_domains_are_refused()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        string[] domains = Enumerable.Range(0, 21).Select((int index) => $"h{index}.example.com").ToArray();

        Assert.False((await setup.Service.IssueAsync(Request(domains), "admin")).Succeeded);
    }

    [Fact]
    public async Task A_missing_webroot_is_explained_before_certbot_is_started()
    {
        Setup setup = await CreateAsync(createWebroot: false);
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.IssueAsync(Request("a.example.com"), "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("webroot", result.Message);
        Assert.Empty(setup.Local.Calls);
    }

    [Fact]
    public async Task Nothing_runs_when_certificate_management_is_disabled()
    {
        Setup setup = await CreateAsync(CertbotMode.Disabled);
        await using NginxTestbed nginx = setup.Nginx;

        Assert.False((await setup.Service.IssueAsync(Request("a.example.com"), "admin")).Succeeded);
        Assert.False((await setup.Service.RenewAsync(null, false, "admin")).Succeeded);
        Assert.False((await setup.Service.DeleteAsync("a.example.com", "admin")).Succeeded);
        Assert.Empty(setup.Local.Calls);
        Assert.Empty(setup.Host.Calls);
    }

    [Fact]
    public async Task When_certbot_fails_its_own_explanation_is_returned()
    {
        Setup setup = await CreateAsync(script: (string _, IReadOnlyList<string> _) => Failed("Challenge failed for domain a.example.com: unauthorized"));
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.IssueAsync(Request("a.example.com"), "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("Challenge failed for domain", result.Output);
    }

    [Fact]
    public async Task Issuing_is_audited_with_the_outcome()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        await setup.Service.IssueAsync(Request("a.example.com"), "oskar");

        IReadOnlyList<AuditEntry> audit = await nginx.Database.Get<IAuditLog>().ListRecentAsync(5);
        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "certbot.issue" && entry.Target == "a.example.com" && entry.Success && entry.Actor == "oskar");
    }

    // ---- Renewing --------------------------------------------------------------------

    [Fact]
    public async Task When_a_certificate_really_changed_nginx_is_reloaded()
    {
        NginxTestbed? bed = null;
        Setup setup = await CreateAsync(script: (string _, IReadOnlyList<string> arguments) =>
        {
            if (arguments[0] == "renew")
            {
                TestCertificates.WriteLive(bed!.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(90));
            }

            return Ok("Congratulations, all renewals succeeded.");
        });
        bed = setup.Nginx;
        await using NginxTestbed nginx = setup.Nginx;
        TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(5));

        CertificateOperation result = await setup.Service.RenewAsync(null, force: false, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.NginxReloaded);
        Assert.Contains("app.example.com", result.Message);
        Assert.Equal(1, nginx.Controller.Reloads);
        string[] arguments2 = setup.Local.Calls.Single().Arguments;
        Assert.Equal(["renew", "--non-interactive"], arguments2.Take(2).ToArray());
    }

    [Fact]
    public async Task When_nothing_was_due_nginx_is_not_reloaded()
    {
        Setup setup = await CreateAsync(script: (string _, IReadOnlyList<string> _) => Ok("No renewals were attempted."));
        await using NginxTestbed nginx = setup.Nginx;
        TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(60));

        CertificateOperation result = await setup.Service.RenewAsync(null, force: false, "admin");

        Assert.True(result.Succeeded);
        Assert.False(result.NginxReloaded);
        Assert.Contains("Nothing to renew", result.Message);
        Assert.Equal(0, nginx.Controller.Reloads);
    }

    [Fact]
    public async Task A_renewal_certbot_could_not_do_is_a_failure_with_the_output()
    {
        Setup setup = await CreateAsync(script: (string _, IReadOnlyList<string> _) => Failed("Failed to renew certificate app.example.com"));
        await using NginxTestbed nginx = setup.Nginx;
        TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(3));

        CertificateOperation result = await setup.Service.RenewAsync(null, force: false, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("Failed to renew", result.Output);
        Assert.Equal(0, nginx.Controller.Reloads);
    }

    [Fact]
    public async Task Forcing_and_targeting_a_certificate_add_the_right_options()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;
        TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(60));

        await setup.Service.RenewAsync("app.example.com", force: true, "admin");

        string[] arguments = setup.Local.Calls.Single().Arguments;
        Assert.Equal("app.example.com", arguments[Array.IndexOf(arguments, "--cert-name") + 1]);
        Assert.Contains("--force-renewal", arguments);
    }

    [Theory]
    [InlineData("--force-renewal")]
    [InlineData("a b")]
    [InlineData("../x")]
    public async Task A_renewal_target_that_is_not_a_certificate_name_never_reaches_certbot(string name)
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.RenewAsync(name, force: false, "admin");

        Assert.False(result.Succeeded);
        Assert.Empty(setup.Local.Calls);
    }

    [Fact]
    public async Task Renewing_a_certificate_that_does_not_exist_is_reported()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.RenewAsync("ghost.example.com", force: false, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("no certificate", result.Message);
        Assert.Empty(setup.Local.Calls);
    }

    [Fact]
    public async Task A_failing_reload_after_a_renewal_is_reported_because_nginx_still_serves_the_old_certificate()
    {
        NginxTestbed? bed = null;
        Setup setup = await CreateAsync(script: (string _, IReadOnlyList<string> arguments) =>
        {
            TestCertificates.WriteLive(bed!.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(90));
            return Ok();
        });
        bed = setup.Nginx;
        await using NginxTestbed nginx = setup.Nginx;
        TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(5));
        nginx.Controller.ReloadFails = true;

        CertificateOperation result = await setup.Service.RenewAsync(null, force: true, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("still serves the old certificate", result.Message);
    }

    // ---- Deleting --------------------------------------------------------------------

    [Fact]
    public async Task An_unused_certificate_is_deleted_through_certbot()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;
        TestCertificates.WriteLive(nginx.LetsEncrypt, "old.example.com", ["old.example.com"], Now.AddDays(60));

        CertificateOperation result = await setup.Service.DeleteAsync("old.example.com", "admin");

        Assert.True(result.Succeeded, result.Message);
        string[] arguments = setup.Local.Calls.Single().Arguments;
        Assert.Equal(["delete", "--cert-name", "old.example.com", "--non-interactive"], arguments.Take(4).ToArray());
    }

    [Fact]
    public async Task A_certificate_that_a_config_uses_cannot_be_deleted()
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;
        string fullChain = TestCertificates.WriteLive(nginx.LetsEncrypt, "app.example.com", ["app.example.com"], Now.AddDays(60)).Replace('\\', '/');
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), $"ssl_certificate {fullChain};\n");

        CertificateOperation result = await setup.Service.DeleteAsync("app.example.com", "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("app.conf", result.Message);
        Assert.Empty(setup.Local.Calls);
    }

    [Theory]
    [InlineData("ghost.example.com")]
    [InlineData("--help")]
    public async Task Only_names_of_existing_certificates_can_be_deleted(string name)
    {
        Setup setup = await CreateAsync();
        await using NginxTestbed nginx = setup.Nginx;

        CertificateOperation result = await setup.Service.DeleteAsync(name, "admin");

        Assert.False(result.Succeeded);
        Assert.Empty(setup.Local.Calls);
    }
}
