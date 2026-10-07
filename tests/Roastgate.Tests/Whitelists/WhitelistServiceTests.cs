using Roastgate.Core.Nginx;
using Roastgate.Core.Nginx.Whitelists;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Whitelists;

public sealed class WhitelistServiceTests
{
    private static IWhitelistService Service(NginxTestbed nginx)
    {
        return nginx.Database.Get<IWhitelistService>();
    }

    private static WhitelistDocument Document(params WhitelistEntry[] entries)
    {
        return new WhitelistDocument(null, entries);
    }

    [Fact]
    public async Task Saving_writes_the_allow_lines_and_reloads_nginx()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await Service(nginx).SaveAsync(
            "alice",
            Document(new WhitelistEntry("203.0.113.7", "home"), new WhitelistEntry("10.0.0.0/8")),
            reload: true,
            "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("allow 203.0.113.7; # home\nallow 10.0.0.0/8;\n", await File.ReadAllTextAsync(Path.Combine(nginx.Whitelists, "alice.conf")));
        Assert.Equal(1, nginx.Controller.Reloads);
    }

    [Fact]
    public async Task An_invalid_address_blocks_the_save_and_nothing_is_written()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await Service(nginx).SaveAsync("alice", Document(new WhitelistEntry("1.2.3.4"), new WhitelistEntry("999.1.1.1")), reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("999.1.1.1", result.Message);
        Assert.False(File.Exists(Path.Combine(nginx.Whitelists, "alice.conf")));
        Assert.Equal(0, nginx.Controller.Tests);
    }

    [Fact]
    public async Task Warnings_do_not_block_the_save()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await Service(nginx).SaveAsync("alice", Document(new WhitelistEntry("10.0.0.5/24"), new WhitelistEntry("10.0.0.5/24")), reload: false, "admin");

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task An_injection_attempt_through_the_address_cannot_reach_the_file()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await Service(nginx).SaveAsync("alice", Document(new WhitelistEntry("1.2.3.4; allow 0.0.0.0/0")), reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(Path.Combine(nginx.Whitelists, "alice.conf")));
    }

    [Fact]
    public async Task Reading_returns_the_parsed_list_the_include_line_and_where_it_is_used()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await Service(nginx).SaveAsync("alice", Document(new WhitelistEntry("203.0.113.7")), reload: false, "admin");
        string includePath = nginx.Whitelists.Replace('\\', '/') + "/alice.conf";
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), $"server {{\n    include {includePath};\n    deny all;\n}}\n");

        WhitelistDetails? details = await Service(nginx).ReadAsync("alice");

        Assert.NotNull(details);
        Assert.Equal("alice.conf", details.Name);
        Assert.NotNull(details.Parse.Document);
        Assert.Equal("203.0.113.7", Assert.Single(details.Parse.Document.Entries).Address);
        Assert.Equal("app.conf", Assert.Single(details.Usage).FileName);
        Assert.StartsWith("include ", details.IncludeSnippet);
        Assert.EndsWith("/alice.conf;", details.IncludeSnippet.Replace('\\', '/'));
    }

    [Fact]
    public async Task Reading_a_missing_whitelist_returns_null()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        Assert.Null(await Service(nginx).ReadAsync("ghost"));
    }

    [Fact]
    public async Task A_file_with_other_directives_is_read_as_unstructured_and_left_untouched()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        const string original = "allow 1.2.3.4;\ndeny 5.6.7.8;\n";
        await File.WriteAllTextAsync(Path.Combine(nginx.Whitelists, "mixed.conf"), original);

        WhitelistDetails? details = await Service(nginx).ReadAsync("mixed");

        Assert.NotNull(details);
        Assert.Null(details.Parse.Document);
        Assert.NotEmpty(details.Parse.Unsupported);
        Assert.Equal(original, details.RawContent);
    }

    [Fact]
    public async Task The_list_shows_counts_usage_and_flags_files_it_cannot_count()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await Service(nginx).SaveAsync("alice", Document(new WhitelistEntry("1.2.3.4"), new WhitelistEntry("5.6.7.8")), reload: false, "admin");
        await File.WriteAllTextAsync(Path.Combine(nginx.Whitelists, "mixed.conf"), "deny all;\n");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), $"include {nginx.Whitelists.Replace('\\', '/')}/alice.conf;\n");

        IReadOnlyList<WhitelistSummary> list = await Service(nginx).ListAsync();

        WhitelistSummary alice = list.Single((WhitelistSummary item) => item.Name == "alice.conf");
        Assert.Equal(2, alice.EntryCount);
        Assert.Single(alice.Usage);
        WhitelistSummary mixed = list.Single((WhitelistSummary item) => item.Name == "mixed.conf");
        Assert.Null(mixed.EntryCount);
        Assert.Empty(mixed.Usage);
    }

    [Fact]
    public async Task Deleting_is_refused_while_a_config_includes_the_whitelist_and_names_it()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await Service(nginx).SaveAsync("alice", Document(new WhitelistEntry("1.2.3.4")), reload: false, "admin");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), $"include {nginx.Whitelists.Replace('\\', '/')}/alice.conf;\n");

        ConfigResult result = await Service(nginx).DeleteAsync("alice", reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("app.conf", result.Message);
        Assert.True(File.Exists(Path.Combine(nginx.Whitelists, "alice.conf")));
    }

    [Fact]
    public async Task Deleting_works_once_nothing_includes_the_whitelist()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await Service(nginx).SaveAsync("alice", Document(new WhitelistEntry("1.2.3.4")), reload: false, "admin");

        ConfigResult result = await Service(nginx).DeleteAsync("alice", reload: true, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(Path.Combine(nginx.Whitelists, "alice.conf")));
    }

    [Fact]
    public async Task A_deletion_that_the_scanner_missed_is_still_caught_by_nginx_and_undone()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await Service(nginx).SaveAsync("alice", Document(new WhitelistEntry("1.2.3.4")), reload: false, "admin");
        // Included from a file Roastgate does not scan (nginx.conf, for instance): only nginx -t can notice.
        nginx.Controller.FailureRule = () => File.Exists(Path.Combine(nginx.Whitelists, "alice.conf")) ? null : "nginx: [emerg] open() \"alice.conf\" failed (2: No such file or directory)";

        ConfigResult result = await Service(nginx).DeleteAsync("alice", reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.True(File.Exists(Path.Combine(nginx.Whitelists, "alice.conf")));
    }

    [Fact]
    public async Task Deleting_a_name_that_is_not_valid_is_reported_as_missing()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await Service(nginx).DeleteAsync("../etc/passwd", reload: true, "admin");

        Assert.False(result.Succeeded);
    }
}
