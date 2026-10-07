using Roastgate.Core.Nginx;
using Roastgate.Core.Nginx.Whitelists;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Whitelists;

public sealed class WhitelistUsageScannerTests
{
    private static async Task<IReadOnlyList<WhitelistUsage>> FindAsync(NginxTestbed nginx, string whitelist)
    {
        IWhitelistUsageScanner scanner = nginx.Database.Get<IWhitelistUsageScanner>();
        IReadOnlyDictionary<string, IReadOnlyList<WhitelistUsage>> usage = await scanner.FindAsync([whitelist]);
        return usage[whitelist];
    }

    private static string Include(NginxTestbed nginx, string name)
    {
        return $"    include {nginx.Whitelists.Replace('\\', '/')}/{name};\n";
    }

    [Fact]
    public async Task An_absolute_include_in_a_reverse_proxy_is_found_with_its_line()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), "server {\n" + Include(nginx, "alice.conf") + "    deny all;\n}\n");

        IReadOnlyList<WhitelistUsage> usage = await FindAsync(nginx, "alice.conf");

        WhitelistUsage found = Assert.Single(usage);
        Assert.Equal(ConfigArea.ReverseProxies, found.Area);
        Assert.Equal("app.conf", found.FileName);
        Assert.Equal(2, found.Line);
        Assert.True(found.FileEnabled);
        Assert.False(found.ViaWildcard);
    }

    [Theory]
    [InlineData("whitelists/alice.conf")]
    [InlineData("./whitelists/alice.conf")]
    [InlineData("conf.d/../whitelists/alice.conf")]
    [InlineData("\"whitelists/alice.conf\"")]
    [InlineData("'whitelists/alice.conf'")]
    public async Task Relative_and_quoted_includes_resolve_against_the_nginx_directory(string include)
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), $"server {{\n    include {include};\n}}\n");

        Assert.Single(await FindAsync(nginx, "alice.conf"));
    }

    [Fact]
    public async Task Only_the_whitelist_that_is_included_is_reported()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), Include(nginx, "alice.conf"));

        IReadOnlyDictionary<string, IReadOnlyList<WhitelistUsage>> usage = await nginx.Database.Get<IWhitelistUsageScanner>().FindAsync(["alice.conf", "bob.conf"]);

        Assert.Single(usage["alice.conf"]);
        Assert.Empty(usage["bob.conf"]);
    }

    [Fact]
    public async Task Commented_out_includes_and_other_include_targets_do_not_count()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(
            Path.Combine(nginx.ConfD, "app.conf"),
            "# " + Include(nginx, "alice.conf").TrimStart() + "    include /etc/nginx/snippets/alice.conf;\n    include mime.types;\n    # include whitelists/alice.conf;\n");

        Assert.Empty(await FindAsync(nginx, "alice.conf"));
    }

    [Fact]
    public async Task An_include_followed_by_a_comment_still_counts()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), Include(nginx, "alice.conf").TrimEnd('\n') + " # office\n");

        Assert.Single(await FindAsync(nginx, "alice.conf"));
    }

    [Fact]
    public async Task A_wildcard_include_matches_every_whitelist_and_says_so()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), "    include whitelists/*.conf;\n");

        WhitelistUsage found = Assert.Single(await FindAsync(nginx, "alice.conf"));

        Assert.True(found.ViaWildcard);
    }

    [Fact]
    public async Task A_wildcard_that_cannot_match_is_ignored()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), "    include whitelists/team-*.conf;\n    include snippets/*.conf;\n");

        Assert.Empty(await FindAsync(nginx, "alice.conf"));
    }

    [Fact]
    public async Task Includes_in_a_disabled_proxy_are_reported_as_not_active()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "old.conf.disabled"), Include(nginx, "alice.conf"));

        WhitelistUsage found = Assert.Single(await FindAsync(nginx, "alice.conf"));

        Assert.Equal("old.conf", found.FileName);
        Assert.False(found.FileEnabled);
    }

    [SymlinkFact]
    public async Task Includes_in_sites_report_whether_the_site_is_enabled()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.Sites, "live.example", "server {\n" + Include(nginx, "alice.conf") + "}\n", reload: false, "admin");
        await nginx.Store.SaveAsync(ConfigArea.Sites, "draft.example", "server {\n" + Include(nginx, "alice.conf") + "}\n", reload: false, "admin");
        await nginx.Store.SetEnabledAsync(ConfigArea.Sites, "live.example", enabled: true, reload: false, "admin");

        IReadOnlyList<WhitelistUsage> usage = await FindAsync(nginx, "alice.conf");

        Assert.Equal(2, usage.Count);
        Assert.True(usage.Single((WhitelistUsage item) => item.FileName == "live.example").FileEnabled);
        Assert.False(usage.Single((WhitelistUsage item) => item.FileName == "draft.example").FileEnabled);
        Assert.All(usage, (WhitelistUsage item) => Assert.Equal(ConfigArea.Sites, item.Area));
    }

    [Fact]
    public async Task Several_includes_in_one_file_are_all_reported_in_line_order()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(
            Path.Combine(nginx.ConfD, "app.conf"),
            "server {\n    location /a {\n" + Include(nginx, "alice.conf") + "    }\n    location /b {\n" + Include(nginx, "alice.conf") + "    }\n}\n");

        IReadOnlyList<WhitelistUsage> usage = await FindAsync(nginx, "alice.conf");

        Assert.Equal([3, 6], usage.Select((WhitelistUsage item) => item.Line).ToArray());
    }

    [Fact]
    public async Task Files_that_are_not_managed_configs_are_not_scanned()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "notes.txt"), Include(nginx, "alice.conf"));
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "has space.conf"), Include(nginx, "alice.conf"));

        Assert.Empty(await FindAsync(nginx, "alice.conf"));
    }

    [Fact]
    public async Task Missing_directories_are_not_an_error()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync(createDirectories: false);

        Assert.Empty(await FindAsync(nginx, "alice.conf"));
    }

    [Theory]
    [InlineData("/etc/nginx/whitelists/a.conf", "/etc/nginx/whitelists/a.conf")]
    [InlineData("whitelists/a.conf", "/etc/nginx/whitelists/a.conf")]
    [InlineData("./whitelists//a.conf", "/etc/nginx/whitelists/a.conf")]
    [InlineData("conf.d/../whitelists/a.conf", "/etc/nginx/whitelists/a.conf")]
    [InlineData("/etc/nginx/./whitelists/x/../a.conf", "/etc/nginx/whitelists/a.conf")]
    [InlineData("/../../a.conf", "/a.conf")]
    public void Paths_are_compared_in_a_normalised_form(string path, string expected)
    {
        Assert.Equal(expected, WhitelistUsageScanner.NormalizePath(path, "/etc/nginx/"));
    }
}
