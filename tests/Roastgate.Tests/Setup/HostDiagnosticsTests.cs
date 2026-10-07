using Roastgate.Core;
using Roastgate.Core.Hosting;
using Roastgate.Core.Settings;
using Roastgate.Core.Setup;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Setup;

public sealed class HostDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roastgate-nginx-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AppSettings CreateLayout(bool withDirectories = true, CertbotMode mode = CertbotMode.Embedded)
    {
        if (withDirectories)
        {
            foreach (string directory in new[] { "conf.d", "sites-available", "sites-enabled", "whitelists" })
            {
                Directory.CreateDirectory(Path.Combine(_root, directory));
            }

            Directory.CreateDirectory(Path.Combine(_root, "acme"));
        }

        return new AppSettings
        {
            NginxRoot = _root,
            WhitelistsDirectory = Path.Combine(_root, "whitelists"),
            AcmeWebroot = Path.Combine(_root, "acme"),
            CertbotMode = mode,
        };
    }

    private static HostDiagnostics CreateDiagnostics(HostExecMode mode, Func<string, IReadOnlyList<string>, HostCommandResult> script)
    {
        ScriptedHostExec exec = new(script);
        return new HostDiagnostics(exec, new NginxProbe(exec), new RoastgateOptions { HostExecMode = mode });
    }

    private static HostCommandResult HealthyHost(string program, IReadOnlyList<string> arguments)
    {
        return program switch
        {
            "true" => ScriptedHostExec.Ok(),
            "nginx" when arguments.SequenceEqual(["-v"]) => ScriptedHostExec.Ok(standardError: "nginx version: nginx/1.27.3"),
            "nginx" => ScriptedHostExec.Ok(standardError: "syntax is ok"),
            _ => ScriptedHostExec.NotFound(),
        };
    }

    private static DiagnosticCheck Find(IReadOnlyList<DiagnosticCheck> checks, string name)
    {
        return checks.Single((DiagnosticCheck check) => check.Name == name);
    }

    [Fact]
    public async Task A_healthy_host_has_no_errors()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Nsenter, HealthyHost);

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout());

        Assert.DoesNotContain(checks, (DiagnosticCheck check) => check.Status != DiagnosticStatus.Ok);
        Assert.Contains("1.27.3", Find(checks, "nginx").Message);
    }

    [Fact]
    public async Task Direct_mode_is_flagged_as_development_only()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Direct, HealthyHost);

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout());

        Assert.Equal(DiagnosticStatus.Warning, Find(checks, "host access").Status);
    }

    [Fact]
    public async Task A_failing_nsenter_is_an_error_that_explains_the_container_requirements()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Nsenter, (string program, IReadOnlyList<string> arguments) =>
            program == "true" ? new HostCommandResult(1, string.Empty, "nsenter: reassociate to namespace failed", false) : HealthyHost(program, arguments));

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout());

        DiagnosticCheck access = Find(checks, "host access");
        Assert.Equal(DiagnosticStatus.Error, access.Status);
        Assert.Contains("pid: host", access.Message);
    }

    [Fact]
    public async Task A_missing_nginx_is_an_error_and_skips_the_configuration_test()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Nsenter, (string program, IReadOnlyList<string> arguments) =>
            program == "nginx" ? ScriptedHostExec.NotFound() : HealthyHost(program, arguments));

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout());

        Assert.Equal(DiagnosticStatus.Error, Find(checks, "nginx").Status);
        Assert.DoesNotContain(checks, (DiagnosticCheck check) => check.Name == "nginx configuration");
    }

    [Fact]
    public async Task A_broken_nginx_configuration_is_reported_with_nginx_output()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Nsenter, (string program, IReadOnlyList<string> arguments) =>
            program == "nginx" && arguments.SequenceEqual(["-t"])
                ? new HostCommandResult(1, string.Empty, "nginx: [emerg] unknown directive \"foo\"", false)
                : HealthyHost(program, arguments));

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout());

        DiagnosticCheck configuration = Find(checks, "nginx configuration");
        Assert.Equal(DiagnosticStatus.Error, configuration.Status);
        Assert.Contains("unknown directive", configuration.Message);
    }

    [Fact]
    public async Task Missing_required_directories_are_errors_and_a_missing_whitelists_directory_is_only_a_warning()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Nsenter, HealthyHost);

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout(withDirectories: false));

        Assert.Equal(DiagnosticStatus.Error, Find(checks, "conf.d").Status);
        Assert.Equal(DiagnosticStatus.Error, Find(checks, "sites-available").Status);
        Assert.Equal(DiagnosticStatus.Error, Find(checks, "sites-enabled").Status);
        Assert.Equal(DiagnosticStatus.Warning, Find(checks, "whitelists").Status);
        Assert.Equal(DiagnosticStatus.Warning, Find(checks, "ACME webroot").Status);
    }

    [Fact]
    public async Task Embedded_certbot_with_a_host_certbot_present_warns_about_double_renewal()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Nsenter, (string program, IReadOnlyList<string> arguments) =>
            program == "certbot" ? ScriptedHostExec.Ok("certbot 2.11.0") : HealthyHost(program, arguments));

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout(mode: CertbotMode.Embedded));

        DiagnosticCheck certbot = Find(checks, "certbot");
        Assert.Equal(DiagnosticStatus.Warning, certbot.Status);
        Assert.Contains("only one", certbot.Message);
    }

    [Fact]
    public async Task Host_mode_requires_certbot_on_the_host()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Nsenter, HealthyHost);

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout(mode: CertbotMode.Host));

        Assert.Equal(DiagnosticStatus.Error, Find(checks, "certbot").Status);
    }

    [Fact]
    public async Task Host_mode_with_certbot_installed_is_fine()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Nsenter, (string program, IReadOnlyList<string> arguments) =>
            program == "certbot" ? ScriptedHostExec.Ok("certbot 2.11.0") : HealthyHost(program, arguments));

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout(mode: CertbotMode.Host));

        Assert.Equal(DiagnosticStatus.Ok, Find(checks, "certbot").Status);
    }

    [Fact]
    public async Task Disabled_certificate_management_never_complains_about_certbot()
    {
        HostDiagnostics diagnostics = CreateDiagnostics(HostExecMode.Nsenter, HealthyHost);

        IReadOnlyList<DiagnosticCheck> checks = await diagnostics.RunAsync(CreateLayout(mode: CertbotMode.Disabled));

        Assert.Equal(DiagnosticStatus.Ok, Find(checks, "certbot").Status);
    }
}
