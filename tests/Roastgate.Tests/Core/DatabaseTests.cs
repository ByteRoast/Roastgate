using Microsoft.EntityFrameworkCore;
using Roastgate.Core.Data;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Core;

public sealed class DatabaseTests
{
    [Fact]
    public async Task Migrating_twice_is_harmless()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();

        await database.Services.MigrateRoastgateDatabaseAsync();

        ISettingsStore settings = database.Get<ISettingsStore>();
        await settings.SetAsync("key", "value");
        Assert.Equal("value", await settings.GetAsync("key"));
    }

    [Fact]
    public async Task The_model_has_no_pending_changes_compared_to_the_migrations()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        IDbContextFactory<RoastgateDbContext> factory = database.Get<IDbContextFactory<RoastgateDbContext>>();

        await using RoastgateDbContext context = await factory.CreateDbContextAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The EF model changed without a migration. Run: dotnet dotnet-ef migrations add <Name> --project src/Roastgate.Core --startup-project src/Roastgate.Web --output-dir Data/Migrations");
    }

    [Fact]
    public async Task Settings_can_be_created_updated_and_deleted()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        ISettingsStore settings = database.Get<ISettingsStore>();

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
        await using TestDatabase database = await TestDatabase.CreateAsync();
        IAuditLog audit = database.Get<IAuditLog>();

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
        await using TestDatabase database = await TestDatabase.CreateAsync();
        IAuditLog audit = database.Get<IAuditLog>();
        for (int index = 0; index < 5; index++)
        {
            await audit.WriteAsync("admin", "action-" + index, null, true);
        }

        IReadOnlyList<AuditEntry> entries = await audit.ListRecentAsync(3);

        Assert.Equal(3, entries.Count);
        Assert.Equal("action-4", entries[0].Action);
    }

    [Fact]
    public async Task Only_one_administrator_can_ever_be_created()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        IAdminAccountStore accounts = database.Get<IAdminAccountStore>();

        Assert.Null(await accounts.GetAsync());
        Assert.True(await accounts.CreateAsync("admin", "hash-1"));
        Assert.False(await accounts.CreateAsync("someone-else", "hash-2"));

        AdminAccount? account = await accounts.GetAsync();
        Assert.NotNull(account);
        Assert.Equal("admin", account.Username);
        Assert.Equal("hash-1", account.PasswordHash);
        Assert.Null(account.TotpSecret);
    }

    [Fact]
    public async Task The_password_hash_can_be_replaced()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        IAdminAccountStore accounts = database.Get<IAdminAccountStore>();
        await accounts.CreateAsync("admin", "old-hash");

        await accounts.UpdatePasswordHashAsync("new-hash");

        Assert.Equal("new-hash", (await accounts.GetAsync())!.PasswordHash);
    }
}
