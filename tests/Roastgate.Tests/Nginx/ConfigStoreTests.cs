using Roastgate.Core.Data;
using Roastgate.Core.Nginx;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Nginx;

public sealed class ConfigStoreTests
{
    private const string GoodProxy = "server { listen 80; server_name app.example.com; }";
    private const string BrokenProxy = "server { " + NginxTestbed.BrokenDirective + " on; }";

    // ---- Saving ----------------------------------------------------------------------

    [Fact]
    public async Task Saving_a_new_reverse_proxy_writes_it_tests_it_and_reloads_nginx()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: true, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Reloaded);
        Assert.Equal(GoodProxy + "\n", await File.ReadAllTextAsync(Path.Combine(nginx.ConfD, "app.conf")));
        Assert.Equal(1, nginx.Controller.Tests);
        Assert.Equal(1, nginx.Controller.Reloads);
    }

    [Fact]
    public async Task The_file_content_is_normalised_to_unix_line_endings_with_one_trailing_newline()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app.conf", "a;\r\nb;\r\n\r\n\r\n", reload: false, "admin");

        Assert.Equal("a;\nb;\n", await File.ReadAllTextAsync(Path.Combine(nginx.ConfD, "app.conf")));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".hidden")]
    [InlineData("-dash")]
    [InlineData("app.disabled")]
    [InlineData("app.conf.disabled")]
    [InlineData("with space")]
    [InlineData("semi;colon")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Unsafe_names_are_refused_and_nothing_is_written(string name)
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, name, GoodProxy, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.Empty(Directory.EnumerateFileSystemEntries(nginx.ConfD));
        Assert.Equal(0, nginx.Controller.Tests);
    }

    [Fact]
    public async Task A_change_nginx_refuses_is_undone_and_the_reason_is_returned()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "broken", BrokenProxy, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.Contains("unknown directive", result.NginxOutput);
        Assert.Contains("rejected", result.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(nginx.ConfD));
        Assert.Equal(0, nginx.Controller.Reloads);
    }

    [Fact]
    public async Task A_refused_edit_restores_the_previous_content_exactly()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: false, "admin");

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", BrokenProxy, reload: true, "admin");

        Assert.True(result.RolledBack);
        Assert.Equal(GoodProxy + "\n", await File.ReadAllTextAsync(Path.Combine(nginx.ConfD, "app.conf")));
    }

    [Fact]
    public async Task No_temporary_file_is_left_behind()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "ok", GoodProxy, reload: false, "admin");
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "bad", BrokenProxy, reload: false, "admin");

        Assert.Equal(["ok.conf"], Directory.EnumerateFiles(nginx.ConfD).Select((string path) => Path.GetFileName(path)!).ToArray());
    }

    [Fact]
    public async Task Overwriting_a_file_keeps_a_backup_of_what_it_replaced()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", "first;", reload: false, "admin");

        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", "second;", reload: false, "admin");

        IReadOnlyList<ConfigBackup> backups = await nginx.Database.Get<IConfigBackupStore>().ListAsync("ReverseProxies", "app.conf", 10);
        ConfigBackup backup = Assert.Single(backups);
        Assert.Equal("first;\n", backup.Content);
        Assert.Equal("save", backup.Reason);
    }

    [Fact]
    public async Task A_change_nginx_refuses_leaves_no_backup_because_it_would_only_repeat_the_current_file()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: false, "admin");

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", BrokenProxy, reload: true, "admin");

        Assert.True(result.RolledBack);
        Assert.Empty(await nginx.Database.Get<IConfigBackupStore>().ListAsync("ReverseProxies", "app.conf", 10));
    }

    [Fact]
    public async Task A_deletion_nginx_refuses_leaves_no_backup_either()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.Whitelists, "office", "allow 203.0.113.0/24;", reload: false, "admin");
        nginx.Controller.FailureRule = () => File.Exists(Path.Combine(nginx.Whitelists, "office.conf")) ? null : "nginx: [emerg] open() failed";

        ConfigResult result = await nginx.Store.DeleteAsync(ConfigArea.Whitelists, "office", reload: true, "admin");

        Assert.True(result.RolledBack);
        Assert.Empty(await nginx.Database.Get<IConfigBackupStore>().ListAsync("Whitelists", "office.conf", 10));
    }

    [Fact]
    public async Task A_change_that_succeeds_keeps_its_backup_even_when_an_earlier_one_was_refused()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", "one;", reload: false, "admin");
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", BrokenProxy, reload: false, "admin");
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", "two;", reload: false, "admin");

        ConfigBackup backup = Assert.Single(await nginx.Database.Get<IConfigBackupStore>().ListAsync("ReverseProxies", "app.conf", 10));

        Assert.Equal("one;\n", backup.Content);
    }

    [Fact]
    public async Task Only_the_most_recent_backups_of_a_file_are_kept()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        for (int version = 0; version <= ConfigBackupStore.KeptPerFile + 5; version++)
        {
            await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", "version " + version + ";", reload: false, "admin");
        }

        IReadOnlyList<ConfigBackup> backups = await nginx.Database.Get<IConfigBackupStore>().ListAsync("ReverseProxies", "app.conf", 100);
        Assert.Equal(ConfigBackupStore.KeptPerFile, backups.Count);
        Assert.Equal("version " + (ConfigBackupStore.KeptPerFile + 4) + ";\n", backups[0].Content);
    }

    [Fact]
    public async Task Saving_without_reload_does_not_reload_nginx()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: false, "admin");

        Assert.True(result.Succeeded);
        Assert.False(result.Reloaded);
        Assert.Equal(0, nginx.Controller.Reloads);
        Assert.Contains("not reloaded", result.Message);
    }

    [Fact]
    public async Task A_failing_reload_still_reports_the_file_as_saved_but_says_the_reload_failed()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        nginx.Controller.ReloadFails = true;

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: true, "admin");

        Assert.True(result.Succeeded);
        Assert.False(result.Reloaded);
        Assert.Contains("reloading nginx failed", result.Message);
        Assert.True(result.Warning);
        Assert.True(File.Exists(Path.Combine(nginx.ConfD, "app.conf")));
    }

    [Fact]
    public async Task When_nginx_is_already_broken_elsewhere_the_change_is_undone_and_the_message_says_so()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "legacy.conf"), BrokenProxy);

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.Contains("broken elsewhere", result.Message);
        Assert.False(File.Exists(Path.Combine(nginx.ConfD, "app.conf")));
    }

    [Fact]
    public async Task Content_over_one_mebibyte_is_refused()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "huge", new string('a', 1024 * 1024 + 1), reload: false, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("1 MiB", result.Message);
    }

    [Fact]
    public async Task Saving_creates_the_whitelists_directory_when_it_is_missing()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync(createDirectories: false);
        Directory.CreateDirectory(nginx.ConfD);

        ConfigResult result = await nginx.Store.SaveAsync(ConfigArea.Whitelists, "alice", "allow 203.0.113.7;", reload: false, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(Path.Combine(nginx.Whitelists, "alice.conf")));
    }

    [Fact]
    public async Task Simultaneous_changes_are_serialised_and_all_succeed()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult[] results = await Task.WhenAll(Enumerable.Range(0, 12).Select(
            (int index) => nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app-" + index, GoodProxy, reload: true, "admin")));

        Assert.All(results, (ConfigResult result) => Assert.True(result.Succeeded, result.Message));
        Assert.Equal(12, Directory.EnumerateFiles(nginx.ConfD).Count());
        Assert.Equal(12, nginx.Controller.Reloads);
    }

    // ---- Deleting --------------------------------------------------------------------

    [Fact]
    public async Task Deleting_removes_the_file_but_keeps_a_backup()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: false, "admin");

        ConfigResult result = await nginx.Store.DeleteAsync(ConfigArea.ReverseProxies, "app", reload: true, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(Path.Combine(nginx.ConfD, "app.conf")));
        IReadOnlyList<ConfigBackup> backups = await nginx.Database.Get<IConfigBackupStore>().ListAsync("ReverseProxies", "app.conf", 10);
        Assert.Contains(backups, (ConfigBackup backup) => backup.Reason == "delete" && backup.Content == GoodProxy + "\n");
    }

    [Fact]
    public async Task A_deletion_nginx_refuses_is_undone()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.Whitelists, "office", "allow 203.0.113.0/24;", reload: false, "admin");
        nginx.Controller.FailureRule = () => File.Exists(Path.Combine(nginx.Whitelists, "office.conf")) ? null : "nginx: [emerg] open() \"office.conf\" failed";

        ConfigResult result = await nginx.Store.DeleteAsync(ConfigArea.Whitelists, "office", reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.Equal("allow 203.0.113.0/24;\n", await File.ReadAllTextAsync(Path.Combine(nginx.Whitelists, "office.conf")));
    }

    [Fact]
    public async Task Deleting_a_file_that_does_not_exist_is_reported()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await nginx.Store.DeleteAsync(ConfigArea.ReverseProxies, "ghost", reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Message);
    }

    // ---- Enabling and disabling reverse proxies --------------------------------------

    [Fact]
    public async Task A_reverse_proxy_is_disabled_by_renaming_it_and_enabled_by_renaming_it_back()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: false, "admin");

        ConfigResult disabled = await nginx.Store.SetEnabledAsync(ConfigArea.ReverseProxies, "app.conf", enabled: false, reload: true, "admin");

        Assert.True(disabled.Succeeded, disabled.Message);
        Assert.False(File.Exists(Path.Combine(nginx.ConfD, "app.conf")));
        Assert.True(File.Exists(Path.Combine(nginx.ConfD, "app.conf.disabled")));
        ConfigFile? file = await nginx.Store.ReadAsync(ConfigArea.ReverseProxies, "app.conf");
        Assert.NotNull(file);
        Assert.False(file.Enabled);

        ConfigResult enabled = await nginx.Store.SetEnabledAsync(ConfigArea.ReverseProxies, "app.conf", enabled: true, reload: true, "admin");

        Assert.True(enabled.Succeeded, enabled.Message);
        Assert.True(File.Exists(Path.Combine(nginx.ConfD, "app.conf")));
        Assert.False(File.Exists(Path.Combine(nginx.ConfD, "app.conf.disabled")));
    }

    [Fact]
    public async Task Enabling_a_proxy_nginx_refuses_leaves_it_disabled()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "old.conf.disabled"), BrokenProxy);

        ConfigResult result = await nginx.Store.SetEnabledAsync(ConfigArea.ReverseProxies, "old.conf", enabled: true, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.True(File.Exists(Path.Combine(nginx.ConfD, "old.conf.disabled")));
        Assert.False(File.Exists(Path.Combine(nginx.ConfD, "old.conf")));
    }

    [Fact]
    public async Task Asking_for_the_state_a_proxy_already_has_changes_nothing()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: false, "admin");
        int testsBefore = nginx.Controller.Tests;

        ConfigResult result = await nginx.Store.SetEnabledAsync(ConfigArea.ReverseProxies, "app", enabled: true, reload: true, "admin");

        Assert.True(result.Succeeded);
        Assert.Contains("Already", result.Message);
        Assert.Equal(testsBefore, nginx.Controller.Tests);
    }

    [Fact]
    public async Task Whitelists_cannot_be_enabled_or_disabled()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.Whitelists, "alice", "allow 203.0.113.7;", reload: false, "admin");

        ConfigResult result = await nginx.Store.SetEnabledAsync(ConfigArea.Whitelists, "alice", enabled: false, reload: true, "admin");

        Assert.False(result.Succeeded);
    }

    // ---- Sites and symlinks ----------------------------------------------------------

    [SymlinkFact]
    public async Task Enabling_a_site_creates_a_relative_symlink_and_disabling_removes_only_the_link()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.Sites, "example.com", GoodProxy, reload: false, "admin");
        Assert.False((await nginx.Store.ReadAsync(ConfigArea.Sites, "example.com"))!.Enabled);

        ConfigResult enabled = await nginx.Store.SetEnabledAsync(ConfigArea.Sites, "example.com", enabled: true, reload: true, "admin");

        Assert.True(enabled.Succeeded, enabled.Message);
        string link = Path.Combine(nginx.SitesEnabled, "example.com");
        Assert.Equal(Path.Combine("..", "sites-available", "example.com"), new FileInfo(link).LinkTarget);
        Assert.True((await nginx.Store.ReadAsync(ConfigArea.Sites, "example.com"))!.Enabled);

        ConfigResult disabled = await nginx.Store.SetEnabledAsync(ConfigArea.Sites, "example.com", enabled: false, reload: true, "admin");

        Assert.True(disabled.Succeeded, disabled.Message);
        Assert.False(File.Exists(link));
        Assert.True(File.Exists(Path.Combine(nginx.SitesAvailable, "example.com")));
    }

    [SymlinkFact]
    public async Task Enabling_a_broken_site_is_undone()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.SitesAvailable, "broken.example"), BrokenProxy);

        ConfigResult result = await nginx.Store.SetEnabledAsync(ConfigArea.Sites, "broken.example", enabled: true, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.Empty(Directory.EnumerateFileSystemEntries(nginx.SitesEnabled));
    }

    [SymlinkFact]
    public async Task Deleting_an_enabled_site_removes_its_symlink_too()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.Sites, "example.com", GoodProxy, reload: false, "admin");
        await nginx.Store.SetEnabledAsync(ConfigArea.Sites, "example.com", enabled: true, reload: false, "admin");

        ConfigResult result = await nginx.Store.DeleteAsync(ConfigArea.Sites, "example.com", reload: true, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(nginx.SitesEnabled));
        Assert.Empty(Directory.EnumerateFileSystemEntries(nginx.SitesAvailable));
    }

    [Fact]
    public async Task A_plain_file_in_sites_enabled_is_never_touched()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.Sites, "example.com", GoodProxy, reload: false, "admin");
        string squatter = Path.Combine(nginx.SitesEnabled, "example.com");
        await File.WriteAllTextAsync(squatter, "# a hand-made copy");

        ConfigResult result = await nginx.Store.SetEnabledAsync(ConfigArea.Sites, "example.com", enabled: true, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("not a symlink", result.Message);
        Assert.Equal("# a hand-made copy", await File.ReadAllTextAsync(squatter));
    }

    // ---- Listing and reading ---------------------------------------------------------

    [Fact]
    public async Task Listing_shows_valid_names_sorted_and_flags_disabled_proxies()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "b.conf"), "b;");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "A.conf"), "a;");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "c.conf.disabled"), "c;");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "notes.txt"), "ignored");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "has space.conf"), "ignored");

        ConfigListing listing = await nginx.Store.ListAsync(ConfigArea.ReverseProxies);

        Assert.True(listing.DirectoryExists);
        Assert.Equal(["A.conf", "b.conf", "c.conf"], listing.Entries.Select((ConfigEntry entry) => entry.Name).ToArray());
        Assert.Equal([true, true, false], listing.Entries.Select((ConfigEntry entry) => entry.Enabled).ToArray());
    }

    [Fact]
    public async Task When_both_the_enabled_and_disabled_copy_exist_the_enabled_one_wins()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "a.conf.disabled"), "old;");
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "a.conf"), "current;");

        ConfigListing listing = await nginx.Store.ListAsync(ConfigArea.ReverseProxies);

        ConfigEntry entry = Assert.Single(listing.Entries);
        Assert.True(entry.Enabled);
    }

    [Fact]
    public async Task A_missing_directory_is_reported_rather_than_failing()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync(createDirectories: false);

        ConfigListing listing = await nginx.Store.ListAsync(ConfigArea.ReverseProxies);

        Assert.False(listing.DirectoryExists);
        Assert.Empty(listing.Entries);
    }

    [Fact]
    public async Task Reading_returns_the_content_or_null()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: false, "admin");

        ConfigFile? file = await nginx.Store.ReadAsync(ConfigArea.ReverseProxies, "app");

        Assert.NotNull(file);
        Assert.Equal("app.conf", file.Name);
        Assert.Equal(GoodProxy + "\n", file.Content);
        Assert.Null(await nginx.Store.ReadAsync(ConfigArea.ReverseProxies, "missing"));
        Assert.Null(await nginx.Store.ReadAsync(ConfigArea.ReverseProxies, "../../etc/passwd"));
    }

    // ---- nginx actions and audit -----------------------------------------------------

    [Fact]
    public async Task Testing_nginx_reports_the_result()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        Assert.True((await nginx.Store.TestNginxAsync("admin")).Succeeded);

        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "x.conf"), BrokenProxy);
        ConfigResult failing = await nginx.Store.TestNginxAsync("admin");
        Assert.False(failing.Succeeded);
        Assert.Contains("unknown directive", failing.NginxOutput);
    }

    [Fact]
    public async Task Reloading_is_refused_while_the_configuration_is_invalid()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "x.conf"), BrokenProxy);

        ConfigResult result = await nginx.Store.ReloadNginxAsync("admin");

        Assert.False(result.Succeeded);
        Assert.Equal(0, nginx.Controller.Reloads);
    }

    [Fact]
    public async Task Reloading_a_valid_configuration_reloads_nginx()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await nginx.Store.ReloadNginxAsync("admin");

        Assert.True(result.Succeeded);
        Assert.True(result.Reloaded);
        Assert.Equal(1, nginx.Controller.Reloads);
    }

    [Fact]
    public async Task Every_action_is_audited_with_who_what_and_the_outcome()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", GoodProxy, reload: true, "oskar");
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "broken", BrokenProxy, reload: true, "oskar");
        await nginx.Store.DeleteAsync(ConfigArea.ReverseProxies, "app", reload: true, "oskar");

        IReadOnlyList<AuditEntry> audit = await nginx.Database.Get<IAuditLog>().ListRecentAsync(10);
        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "config.save" && entry.Target == "conf.d/app.conf" && entry.Success && entry.Actor == "oskar");
        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "config.save" && entry.Target == "conf.d/broken.conf" && !entry.Success);
        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "config.delete" && entry.Target == "conf.d/app.conf" && entry.Success);
    }
}
