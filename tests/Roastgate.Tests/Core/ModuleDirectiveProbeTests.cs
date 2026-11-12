using Roastgate.Core.Hosting;
using Roastgate.Core.Nginx.Modules;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Core;

public sealed class ModuleDirectiveProbeTests : IDisposable
{
    private const string CaseSensitive = "fancyindex_case_sensitive";

    private static readonly NginxModules FancyIndexLoaded = NginxModules.Parse(
        "load_module modules/ngx_http_fancyindex_module.so;"
    );

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "roastgate-probe-" + Guid.NewGuid().ToString("N")
    );

    private string ProbeDirectory => Path.Combine(_root, ".roastgate-probe");

    public void Dispose()
    {
        TemporaryDirectory.Delete(_root);
    }

    private static HostCommandResult Failed(string standardError)
    {
        return new HostCommandResult(1, string.Empty, standardError, false);
    }

    private static HostCommandResult UnknownDirective(string name, string path)
    {
        return Failed($"nginx: [emerg] unknown directive \"{name}\" in {path}:9");
    }

    [Fact]
    public async Task Supports_a_directive_nginx_accepts()
    {
        RecordingHostExec hostExec = new((_, _) => ScriptedHostExec.Ok());

        ModuleDirectives directives = await new ModuleDirectiveProbe(hostExec).ProbeAsync(
            FancyIndexLoaded,
            ProbeDirectory,
            CancellationToken.None
        );

        Assert.True(directives.Known);
        Assert.True(directives.Supports(KnownModule.FancyIndex, CaseSensitive));

        (string program, string[] arguments) = Assert.Single(hostExec.Calls);

        Assert.Equal("nginx", program);
        Assert.Equal("-t", arguments[0]);
        Assert.Equal("-c", arguments[1]);
        Assert.Equal(ProbeDirectory, Path.GetDirectoryName(arguments[2]));
    }

    [Fact]
    public async Task Hands_nginx_a_configuration_that_loads_the_modules_and_names_the_directive()
    {
        string? configuration = null;

        RecordingHostExec hostExec = new((_, arguments) =>
            {
                configuration = File.ReadAllText(arguments[2]);

                return ScriptedHostExec.Ok();
            }
        );

        await new ModuleDirectiveProbe(hostExec).ProbeAsync(
            FancyIndexLoaded,
            ProbeDirectory,
            CancellationToken.None
        );

        Assert.NotNull(configuration);
        Assert.Contains("load_module modules/ngx_http_fancyindex_module.so;", configuration);
        Assert.Contains($"{CaseSensitive} on;", configuration);
    }

    [Fact]
    public async Task Does_not_support_a_directive_nginx_reports_as_unknown_and_tests_again_without_it()
    {
        List<string> configurations = [];

        RecordingHostExec hostExec = new((_, arguments) =>
            {
                string configuration = File.ReadAllText(arguments[2]);

                configurations.Add(configuration);

                return configuration.Contains(CaseSensitive)
                    ? UnknownDirective(CaseSensitive, arguments[2])
                    : ScriptedHostExec.Ok();
            }
        );

        ModuleDirectives directives = await new ModuleDirectiveProbe(hostExec).ProbeAsync(
            FancyIndexLoaded,
            ProbeDirectory,
            CancellationToken.None
        );

        Assert.True(directives.Known);
        Assert.False(directives.Supports(KnownModule.FancyIndex, CaseSensitive));
        Assert.Equal(2, configurations.Count);
        Assert.DoesNotContain(CaseSensitive, configurations[1]);
    }

    [Fact]
    public async Task Is_unknown_when_nginx_fails_for_another_reason()
    {
        RecordingHostExec hostExec = new((_, _) => Failed(
                "nginx: [emerg] dlopen() \"/usr/lib/nginx/modules/x.so\" failed"
            )
        );

        ModuleDirectives directives = await new ModuleDirectiveProbe(hostExec).ProbeAsync(
            FancyIndexLoaded,
            ProbeDirectory,
            CancellationToken.None
        );

        Assert.False(directives.Known);
        Assert.Single(hostExec.Calls);
    }

    [Fact]
    public async Task Is_unknown_when_nginx_reports_a_directive_that_was_not_tested()
    {
        RecordingHostExec hostExec = new((_, arguments) => UnknownDirective(
                "something_else",
                arguments[2]
            )
        );

        ModuleDirectives directives = await new ModuleDirectiveProbe(hostExec).ProbeAsync(
            FancyIndexLoaded,
            ProbeDirectory,
            CancellationToken.None
        );

        Assert.False(directives.Known);
        Assert.Single(hostExec.Calls);
    }

    [Fact]
    public async Task Is_unknown_without_running_nginx_when_the_loaded_modules_are_unknown()
    {
        RecordingHostExec hostExec = new((_, _) => ScriptedHostExec.Ok());

        ModuleDirectives directives = await new ModuleDirectiveProbe(hostExec).ProbeAsync(
            NginxModules.Unknown,
            ProbeDirectory,
            CancellationToken.None
        );

        Assert.False(directives.Known);
        Assert.Empty(hostExec.Calls);
    }

    [Fact]
    public async Task Does_not_run_nginx_when_no_loaded_module_has_optional_directives()
    {
        RecordingHostExec hostExec = new((_, _) => ScriptedHostExec.Ok());

        ModuleDirectives directives = await new ModuleDirectiveProbe(hostExec).ProbeAsync(
            NginxModules.Parse(string.Empty),
            ProbeDirectory,
            CancellationToken.None
        );

        Assert.True(directives.Known);
        Assert.False(directives.Supports(KnownModule.FancyIndex, CaseSensitive));
        Assert.Empty(hostExec.Calls);
        Assert.False(Directory.Exists(ProbeDirectory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deletes_the_temporary_configuration_whatever_nginx_answers(bool nginxAccepts)
    {
        RecordingHostExec hostExec = new((_, _) => nginxAccepts
            ? ScriptedHostExec.Ok()
            : Failed("nginx: [emerg] something went wrong")
        );

        await new ModuleDirectiveProbe(hostExec).ProbeAsync(
            FancyIndexLoaded,
            ProbeDirectory,
            CancellationToken.None
        );

        Assert.Empty(Directory.EnumerateFileSystemEntries(ProbeDirectory));
    }

    [Fact]
    public async Task Is_unknown_when_the_temporary_configuration_cannot_be_written()
    {
        // A file where the probe directory should be: creating the directory fails.
        Directory.CreateDirectory(_root);

        await File.WriteAllTextAsync(ProbeDirectory, string.Empty);

        RecordingHostExec hostExec = new((_, _) => ScriptedHostExec.Ok());

        ModuleDirectives directives = await new ModuleDirectiveProbe(hostExec).ProbeAsync(
            FancyIndexLoaded,
            ProbeDirectory,
            CancellationToken.None
        );

        Assert.False(directives.Known);
        Assert.Empty(hostExec.Calls);
    }
}
