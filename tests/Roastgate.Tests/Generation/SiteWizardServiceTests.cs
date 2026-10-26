using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core.Hosting;
using Roastgate.Core.Nginx;
using Roastgate.Core.Nginx.Generation;
using Roastgate.Core.Nginx.Parsing;
using Roastgate.Core.Nginx.Whitelists;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Generation;

public sealed class SiteWizardServiceTests
{
    private static ISiteWizardService Wizard(NginxTestbed nginx)
    {
        return nginx.Database.Get<ISiteWizardService>();
    }

    private static SiteSpec ValidSpec()
    {
        return new SiteSpec
        {
            ServerNames = ["app.example.com"],
            Upstream = "http://127.0.0.1:3000"
        };
    }

    private static SiteSpec FancyIndexSpec()
    {
        return new SiteSpec()
        {
            Kind = SiteKind.StaticSite,
            ServerNames = ["files.example.com"],
            RootDirectory = "/srv/files",
            FancyIndex = new FancyIndexOptions()
        };
    }

    private static Task<NginxTestbed> TestbedWhereNginxSays(Func<HostCommandResult> answer)
    {
        return NginxTestbed.CreateAsync(configureServices: (IServiceCollection services) =>
            services.AddSingleton<IHostExec>(new ScriptedHostExec((string program, IReadOnlyList<string> arguments) =>
                answer())));
    }

    [Fact]
    public async Task A_new_reverse_proxy_spec_starts_as_a_proxy_and_a_new_site_spec_as_a_static_site()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        SiteSpec proxy = await Wizard(nginx).NewSpecAsync(ConfigArea.ReverseProxies);
        SiteSpec site = await Wizard(nginx).NewSpecAsync(ConfigArea.Sites);

        Assert.Equal(SiteKind.ReverseProxy, proxy.Kind);
        Assert.Equal(SiteKind.StaticSite, site.Kind);
        Assert.StartsWith("/var/www/", site.RootDirectory);
    }

    [Fact]
    public async Task Previewing_a_valid_spec_returns_the_text_with_its_header()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        WizardPreview preview = await Wizard(nginx).PreviewAsync(ConfigArea.ReverseProxies, "app", ValidSpec());

        Assert.Empty(preview.Problems);
        Assert.NotNull(preview.Text);
        Assert.Contains("server_name app.example.com;", preview.Text);
        Assert.Contains(SiteMetadataFormat.SpecPrefix, preview.Text);
        Assert.Contains("$rg_upgrade_app", preview.Text);
    }

    [Fact]
    public async Task Previewing_an_invalid_spec_returns_the_problems_and_no_text()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        WizardPreview preview = await Wizard(nginx).PreviewAsync(ConfigArea.ReverseProxies, "app",
            ValidSpec() with { Upstream = "not a url", ServerNames = [] });

        Assert.Null(preview.Text);
        Assert.Contains(preview.Problems, (SpecProblem problem) => problem.Field == nameof(SiteSpec.Upstream));
        Assert.Contains(preview.Problems, (SpecProblem problem) => problem.Field == nameof(SiteSpec.ServerNames));
    }

    [Fact]
    public async Task A_whitelist_that_does_not_exist_is_a_problem_before_nginx_is_even_asked()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        WizardPreview preview = await Wizard(nginx).PreviewAsync(ConfigArea.ReverseProxies, "app",
            ValidSpec() with { Whitelists = ["ghost.conf"] });

        Assert.Null(preview.Text);
        SpecProblem problem = Assert.Single(preview.Problems);
        Assert.Contains("ghost.conf", problem.Message);
        Assert.Equal(0, nginx.Controller.Tests);
    }

    [Fact]
    public async Task A_whitelist_that_exists_is_included_in_the_generated_config()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Database.Get<IWhitelistService>().SaveAsync("alice",
            new WhitelistDocument(null, [new WhitelistEntry("1.2.3.4")]), reload: false, "admin");

        WizardPreview preview = await Wizard(nginx).PreviewAsync(ConfigArea.ReverseProxies, "app",
            ValidSpec() with { Whitelists = ["alice.conf"] });

        Assert.Empty(preview.Problems);
        Assert.Contains("alice.conf;", preview.Text);
        Assert.Contains("deny all;", preview.Text);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("")]
    [InlineData("a b")]
    public async Task An_invalid_file_name_is_refused(string name)
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        WizardPreview preview = await Wizard(nginx).PreviewAsync(ConfigArea.ReverseProxies, name, ValidSpec());

        Assert.Null(preview.Text);
    }

    [Fact]
    public async Task The_wizard_does_not_generate_whitelists()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        WizardPreview preview = await Wizard(nginx).PreviewAsync(ConfigArea.Whitelists, "alice", ValidSpec());

        Assert.Null(preview.Text);
    }

    [Fact]
    public async Task Saving_writes_the_file_reloads_nginx_and_the_wizard_can_read_it_back()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await Wizard(nginx).SaveAsync(ConfigArea.ReverseProxies, "app.example.com", ValidSpec(),
            reload: true, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, nginx.Controller.Reloads);
        string onDisk = await File.ReadAllTextAsync(Path.Combine(nginx.ConfD, "app.example.com.conf"));
        Assert.Contains("proxy_pass http://127.0.0.1:3000;", onDisk);

        WizardFile? loaded = await Wizard(nginx).LoadAsync(ConfigArea.ReverseProxies, "app.example.com");

        Assert.NotNull(loaded);
        Assert.Equal(MetadataStatus.Intact, loaded.Metadata.Status);
        Assert.Equal(["app.example.com"], loaded.Metadata.Spec!.ServerNames);
        Assert.Equal("http://127.0.0.1:3000", loaded.Metadata.Spec.Upstream);
    }

    [Fact]
    public async Task Saving_an_invalid_spec_writes_nothing_and_says_what_is_wrong()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await Wizard(nginx).SaveAsync(ConfigArea.ReverseProxies, "app",
            ValidSpec() with { Upstream = "http://x;" }, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("destination", result.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(nginx.ConfD));
        Assert.Equal(0, nginx.Controller.Tests);
    }

    [Fact]
    public async Task A_config_nginx_refuses_is_rolled_back_like_any_other_save()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        nginx.Controller.FailureRule = () =>
            Directory.EnumerateFiles(nginx.ConfD).Any() ? "nginx: [emerg] a directive the fake does not like" : null;

        ConfigResult result =
            await Wizard(nginx).SaveAsync(ConfigArea.ReverseProxies, "app", ValidSpec(), reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.Empty(Directory.EnumerateFileSystemEntries(nginx.ConfD));
    }

    [Fact]
    public async Task A_hand_edit_after_the_wizard_is_detected_when_the_file_is_reopened()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await Wizard(nginx).SaveAsync(ConfigArea.ReverseProxies, "app", ValidSpec(), reload: false, "admin");
        WizardFile edited = (await Wizard(nginx).LoadAsync(ConfigArea.ReverseProxies, "app"))!;
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app",
            edited.File.Content.Replace("listen 80;", "listen 81;"), reload: false, "admin");

        WizardFile? reopened = await Wizard(nginx).LoadAsync(ConfigArea.ReverseProxies, "app");

        Assert.Equal(MetadataStatus.EditedByHand, reopened!.Metadata.Status);
        Assert.NotNull(reopened.Metadata.Spec);
    }

    [Fact]
    public async Task A_file_written_without_the_wizard_has_no_metadata()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "legacy", "server { listen 80; }", reload: false,
            "admin");

        WizardFile? loaded = await Wizard(nginx).LoadAsync(ConfigArea.ReverseProxies, "legacy");

        Assert.Equal(MetadataStatus.None, loaded!.Metadata.Status);
    }

    [Fact]
    public async Task A_hand_written_file_can_be_imported_and_saved_through_the_wizard_with_a_backup()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        const string handWritten =
            "server {\n    listen 80;\n    server_name legacy.example.com;\n    location / {\n        proxy_pass http://127.0.0.1:9000;\n        proxy_buffering off;\n    }\n}\n";
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "legacy", handWritten, reload: false, "admin");

        SiteImportResult? import = await Wizard(nginx).ImportAsync(ConfigArea.ReverseProxies, "legacy.conf");

        Assert.NotNull(import?.Spec);
        Assert.Equal("proxy_buffering off;", import.Spec.ExtraLocationDirectives);
        ConfigResult saved = await Wizard(nginx)
            .SaveAsync(ConfigArea.ReverseProxies, "legacy", import.Spec, reload: false, "admin");
        Assert.True(saved.Succeeded, saved.Message);
        ConfigFile? after = await nginx.Store.ReadAsync(ConfigArea.ReverseProxies, "legacy");
        Assert.Contains("proxy_buffering off;", after!.Content);
        Assert.Contains("proxy_pass http://127.0.0.1:9000;", after.Content);
        Assert.Equal(MetadataStatus.Intact, SiteMetadataFormat.Read(after.Content).Status);
    }

    [Fact]
    public async Task Importing_a_missing_file_or_a_whitelist_returns_null()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        Assert.Null(await Wizard(nginx).ImportAsync(ConfigArea.ReverseProxies, "ghost"));
        Assert.Null(await Wizard(nginx).ImportAsync(ConfigArea.Whitelists, "ghost"));
    }

    [Fact]
    public async Task Loading_a_missing_file_returns_null()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        Assert.Null(await Wizard(nginx).LoadAsync(ConfigArea.ReverseProxies, "ghost"));
    }

    [Fact]
    public async Task Listings_flag_the_files_the_wizard_can_reopen()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await Wizard(nginx).SaveAsync(ConfigArea.ReverseProxies, "generated", ValidSpec(), reload: false, "admin");
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "handmade", "server { listen 80; }", reload: false,
            "admin");

        ConfigListing listing = await nginx.Store.ListAsync(ConfigArea.ReverseProxies);

        Assert.True(listing.Entries.Single((ConfigEntry entry) => entry.Name == "generated.conf").Wizard);
        Assert.False(listing.Entries.Single((ConfigEntry entry) => entry.Name == "handmade.conf").Wizard);
    } 

    [Fact]
    public async Task Two_wizard_proxies_never_share_a_websocket_variable()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        await Wizard(nginx).SaveAsync(ConfigArea.ReverseProxies, "one", ValidSpec(), reload: false, "admin");
        await Wizard(nginx).SaveAsync(ConfigArea.ReverseProxies, "two",
            ValidSpec() with { ServerNames = ["two.example.com"] }, reload: false, "admin");

        string one = await File.ReadAllTextAsync(Path.Combine(nginx.ConfD, "one.conf"));
        string two = await File.ReadAllTextAsync(Path.Combine(nginx.ConfD, "two.conf"));
        Assert.Contains("$rg_upgrade_one", one);
        Assert.Contains("$rg_upgrade_two", two);
        Assert.DoesNotContain("$rg_upgrade_one", two);
    }

    [Fact]
    public async Task Fancyindex_is_refused_when_the_module_is_not_loaded()
    {
        await using NginxTestbed nginx = await TestbedWhereNginxSays(() => ScriptedHostExec.Ok("user nginx;\nevents {}"));

        WizardPreview preview = await Wizard(nginx).PreviewAsync(ConfigArea.Sites, "files", FancyIndexSpec());

        Assert.Null(preview.Text);
        
        SpecProblem problem = Assert.Single(preview.Problems);
        
        Assert.Equal(nameof(SiteSpec.FancyIndex), problem.Field);
        Assert.Contains("libnginx-mod-http-fancyindex", problem.Message);
    }

    [Fact]
    public async Task Fancyindex_is_accepted_when_the_module_is_loaded()
    {
        await using NginxTestbed nginx = await TestbedWhereNginxSays(
            () => ScriptedHostExec.Ok("load_module modules/ngx_http_fancyindex_module.so;")
        );

        WizardPreview preview = await Wizard(nginx).PreviewAsync(ConfigArea.Sites, "files", FancyIndexSpec());

        Assert.Empty(preview.Problems);
        Assert.Contains("fancyindex on;", preview.Text);
    }

    [Fact]
    public async Task Fancyindex_is_accepted_when_the_module_list_is_unknown()
    {
        await using NginxTestbed nginx = await TestbedWhereNginxSays(ScriptedHostExec.NotFound);

        WizardPreview preview = await Wizard(nginx).PreviewAsync(ConfigArea.Sites, "files", FancyIndexSpec());

        Assert.Empty(preview.Problems);
        Assert.NotNull(preview.Text);
    }

    [Fact]
    public async Task A_site_without_fancyindex_does_not_ask_nginx_for_its_modules()
    {
        int moduleProbes = 0;
        
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync(
            configureServices: (IServiceCollection services) => services.AddSingleton<IHostExec>(
                new ScriptedHostExec((string program, IReadOnlyList<string> arguments) =>
                {
                    if (arguments.SequenceEqual(["-T"]))
                    {
                        moduleProbes++;
                    }

                    return ScriptedHostExec.NotFound();
                })
            )
        );

        await Wizard(nginx).PreviewAsync(ConfigArea.ReverseProxies, "app", ValidSpec());

        Assert.Equal(0, moduleProbes);
    }
}
