using Roastgate.Core;
using Roastgate.Core.Data;

namespace Roastgate.Tests.Core;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "roastgate-tests-" + Guid.NewGuid().ToString("N"));

    private async Task<RoastgateDatabase> CreateMigratedDatabaseAsync()
    {
        RoastgateDatabase database = new(new RoastgateOptions { DataDirectory = _directory });
        await database.MigrateAsync();
        return database;
    }

    [Fact]
    public async Task Migrating_twice_is_harmless()
    {
        RoastgateDatabase database = await CreateMigratedDatabaseAsync();

        await database.MigrateAsync();

        SettingsStore settings = new(database);
        await settings.SetAsync("key", "value");
        Assert.Equal("value", await settings.GetAsync("key"));
    }

    [Fact]
    public async Task Settings_can_be_created_updated_and_deleted()
    {
        SettingsStore settings = new(await CreateMigratedDatabaseAsync());

        Assert.Null(await settings.GetAsync("missing"));

        await settings.SetAsync("theme", "light");
        await settings.SetAsync("theme", "dark");
        Assert.Equal("dark", await settings.GetAsync("theme"));

        Assert.True(await settings.DeleteAsync("theme"));
        Assert.False(await settings.DeleteAsync("theme"));
        Assert.Null(await settings.GetAsync("theme"));
    }

    [Fact]
    public async Task Audit_entries_are_listed_newest_first_and_keep_their_fields()
    {
        AuditLog audit = new(await CreateMigratedDatabaseAsync());

        await audit.WriteAsync("admin", "config.save", "conf.d/app.conf", true);
        await audit.WriteAsync("admin", "nginx.reload", null, false, "nginx: [emerg] unexpected end of file");

        IReadOnlyList<AuditEntry> entries = await audit.ListRecentAsync(10);

        Assert.Equal(2, entries.Count);
        Assert.Equal("nginx.reload", entries[0].Action);
        Assert.False(entries[0].Success);
        Assert.Null(entries[0].Target);
        Assert.Equal("nginx: [emerg] unexpected end of file", entries[0].Details);
        Assert.Equal("conf.d/app.conf", entries[1].Target);
        Assert.True(entries[1].Success);
    }

    [Fact]
    public async Task Audit_listing_honours_the_requested_count()
    {
        AuditLog audit = new(await CreateMigratedDatabaseAsync());
        for (int index = 0; index < 5; index++)
        {
            await audit.WriteAsync("admin", "action-" + index, null, true);
        }

        IReadOnlyList<AuditEntry> entries = await audit.ListRecentAsync(3);

        Assert.Equal(3, entries.Count);
        Assert.Equal("action-4", entries[0].Action);
    }

    public void Dispose()
    {
        // SQLite pools connections per file; release them so the temp directory can be deleted on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
