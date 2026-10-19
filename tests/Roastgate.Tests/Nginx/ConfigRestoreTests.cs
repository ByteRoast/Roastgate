using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;
using Roastgate.Core.Nginx;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Nginx;

public sealed class ConfigRestoreTests
{
    private const string Good = "server { listen 80; }";
    private const string Broken = "server { " + NginxTestbed.BrokenDirective + " on; }";

    private static async Task<long> LatestBackupIdAsync(NginxTestbed nginx, string area, string name)
    {
        IReadOnlyList<ConfigBackup> backups = await nginx.Database.Get<IConfigBackupStore>().ListAsync(area, name, 1);
        
        return backups.Single().Id;
    }

    [Fact]
    public async Task Restoring_puts_the_old_content_back_and_backs_up_what_it_replaced()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", "version one;", reload: false, "admin");
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", "version two;", reload: false, "admin");
        
        long backupOfVersionOne = await LatestBackupIdAsync(nginx, "ReverseProxies", "app.conf");

        ConfigResult result = await nginx.Store.RestoreAsync(backupOfVersionOne, reload: true, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Restored. nginx reloaded.", result.Message);
        Assert.Equal("version one;\n", await File.ReadAllTextAsync(Path.Combine(nginx.ConfD, "app.conf")));
        
        IReadOnlyList<ConfigBackup> backups = await nginx.Database.Get<IConfigBackupStore>().ListAsync("ReverseProxies", "app.conf", 10);
        ConfigBackup restoreBackup = backups[0];
        
        Assert.Equal("restore", restoreBackup.Reason);
        Assert.Equal("version two;\n", restoreBackup.Content);
    }

    [Fact]
    public async Task A_deleted_file_can_be_brought_back()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", Good, reload: false, "admin");
        await nginx.Store.DeleteAsync(ConfigArea.ReverseProxies, "app", reload: false, "admin");
        
        Assert.False(File.Exists(Path.Combine(nginx.ConfD, "app.conf")));
        
        long backupId = await LatestBackupIdAsync(nginx, "ReverseProxies", "app.conf");

        ConfigResult result = await nginx.Store.RestoreAsync(backupId, reload: false, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(Good + "\n", await File.ReadAllTextAsync(Path.Combine(nginx.ConfD, "app.conf")));
    }

    [Fact]
    public async Task A_backup_nginx_no_longer_accepts_is_not_restored_and_the_current_file_stays()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(nginx.ConfD, "app.conf"), Good);
        
        // An old version that used a directive the installed nginx does not know, saved when it was still valid.
        long backupId = await nginx.Database.Get<IConfigBackupStore>().AddAsync("ReverseProxies", "app.conf", "save", Broken);

        ConfigResult result = await nginx.Store.RestoreAsync(backupId, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.Equal(Good, await File.ReadAllTextAsync(Path.Combine(nginx.ConfD, "app.conf")));
    }

    [Fact]
    public async Task An_unknown_backup_is_reported()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();

        ConfigResult result = await nginx.Store.RestoreAsync(123456, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Message);
    }

    [Theory]
    [InlineData("Nonsense", "app.conf")]
    [InlineData("99", "app.conf")]
    [InlineData("ReverseProxies", "../../etc/passwd")]
    [InlineData("ReverseProxies", "with space.conf")]
    public async Task A_tampered_backup_row_cannot_be_used_to_write_elsewhere(string area, string name)
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        
        long backupId = await nginx.Database.Get<IConfigBackupStore>().AddAsync(area, name, "save", Good);

        ConfigResult result = await nginx.Store.RestoreAsync(backupId, reload: true, "admin");

        Assert.False(result.Succeeded);
        Assert.Empty(Directory.EnumerateFileSystemEntries(nginx.ConfD));
        Assert.False(File.Exists(Path.Combine(nginx.Root, "passwd")));
    }

    [Fact]
    public async Task Restoring_is_audited_as_a_restore()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", "one;", reload: false, "admin");
        await nginx.Store.SaveAsync(ConfigArea.ReverseProxies, "app", "two;", reload: false, "admin");
        await nginx.Store.RestoreAsync(await LatestBackupIdAsync(nginx, "ReverseProxies", "app.conf"), reload: false, "oskar");

        IReadOnlyList<AuditEntry> audit = await nginx.Database.Get<IAuditLog>().ListRecentAsync(5);

        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "config.restore" && entry.Target == "conf.d/app.conf" && entry.Actor == "oskar" && entry.Success);
    }

    [Fact]
    public async Task A_whitelist_backup_restores_into_the_whitelists_directory()
    {
        await using NginxTestbed nginx = await NginxTestbed.CreateAsync();
        await nginx.Store.SaveAsync(ConfigArea.Whitelists, "alice", "allow 1.1.1.1;", reload: false, "admin");
        await nginx.Store.SaveAsync(ConfigArea.Whitelists, "alice", "allow 2.2.2.2;", reload: false, "admin");

        ConfigResult result = await nginx.Store.RestoreAsync(await LatestBackupIdAsync(nginx, "Whitelists", "alice.conf"), reload: false, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("allow 1.1.1.1;\n", await File.ReadAllTextAsync(Path.Combine(nginx.Whitelists, "alice.conf")));
    }
}
