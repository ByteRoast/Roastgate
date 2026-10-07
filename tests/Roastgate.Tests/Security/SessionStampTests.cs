using Microsoft.Extensions.Time.Testing;
using Roastgate.Core.Data;
using Roastgate.Core.Security;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Security;

public sealed class SessionStampTests
{
    private const string Password = "Correct-Horse-9";

    private static async Task<TestDatabase> CreateWithAdminAsync()
    {
        TestDatabase database = await TestDatabase.CreateAsync();
        await database.Get<IAdminAccountStore>().CreateAsync("admin", database.Get<PasswordHasher>().Hash(Password));
        return database;
    }

    [Fact]
    public async Task There_is_no_stamp_before_the_administrator_exists()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();

        Assert.Null(await database.Get<ISessionStamp>().CurrentAsync());
    }

    [Fact]
    public async Task A_new_account_gets_a_random_stamp_that_is_stable_between_calls()
    {
        await using TestDatabase database = await CreateWithAdminAsync();
        ISessionStamp stamp = database.Get<ISessionStamp>();

        string? first = await stamp.CurrentAsync();
        string? second = await stamp.CurrentAsync();

        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, second);
        Assert.True(first!.Length >= 20, "the stamp must be long enough to be unguessable");
    }

    [Fact]
    public async Task Two_accounts_never_share_a_stamp()
    {
        await using TestDatabase one = await CreateWithAdminAsync();
        await using TestDatabase two = await CreateWithAdminAsync();

        Assert.NotEqual(await one.Get<ISessionStamp>().CurrentAsync(), await two.Get<ISessionStamp>().CurrentAsync());
    }

    [Fact]
    public async Task Changing_the_password_renews_the_stamp_immediately()
    {
        await using TestDatabase database = await CreateWithAdminAsync();
        ISessionStamp stamp = database.Get<ISessionStamp>();
        string? before = await stamp.CurrentAsync();

        IReadOnlyList<string> problems = await database.Get<AccountService>().ChangePasswordAsync(Password, "A-brand-new-Passw0rd");

        Assert.Empty(problems);
        Assert.NotEqual(before, await stamp.CurrentAsync());
    }

    [Fact]
    public async Task Resetting_the_password_from_the_console_renews_it_too_once_the_cache_has_expired()
    {
        FakeTimeProvider time = new();
        await using TestDatabase database = await TestDatabase.CreateAsync(configureServices: (Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<TimeProvider>(services, time));
        await database.Get<IAdminAccountStore>().CreateAsync("admin", database.Get<PasswordHasher>().Hash(Password));
        ISessionStamp stamp = database.Get<ISessionStamp>();
        string? before = await stamp.CurrentAsync();

        // Another process (the CLI) changes the password: this process does not know yet...
        await database.Get<IAdminAccountStore>().UpdatePasswordHashAsync(database.Get<PasswordHasher>().Hash("Another-Passw0rd-1"));
        Assert.Equal(before, await stamp.CurrentAsync());

        // ...until the cache expires.
        time.Advance(TimeSpan.FromSeconds(11));
        Assert.NotEqual(before, await stamp.CurrentAsync());
    }

    [Fact]
    public async Task Upgrading_a_weak_hash_at_sign_in_keeps_every_session_valid()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        IAdminAccountStore store = database.Get<IAdminAccountStore>();
        await store.CreateAsync("admin", new PasswordHasher(10).Hash(Password));
        ISessionStamp stamp = database.Get<ISessionStamp>();
        string? before = await stamp.CurrentAsync();

        AdminAccount? account = await database.Get<AccountService>().VerifyCredentialsAsync("admin", Password);
        stamp.Invalidate();

        Assert.NotNull(account);
        Assert.StartsWith("pbkdf2-sha512$1000$", (await store.GetAsync())!.PasswordHash);
        Assert.Equal(before, await stamp.CurrentAsync());
    }

    // ---- Changing the password -------------------------------------------------------

    [Fact]
    public async Task The_new_password_works_and_the_old_one_does_not()
    {
        await using TestDatabase database = await CreateWithAdminAsync();
        AccountService accounts = database.Get<AccountService>();

        await accounts.ChangePasswordAsync(Password, "A-brand-new-Passw0rd");

        Assert.Null(await accounts.VerifyCredentialsAsync("admin", Password));
        Assert.NotNull(await accounts.VerifyCredentialsAsync("admin", "A-brand-new-Passw0rd"));
    }

    [Fact]
    public async Task A_wrong_current_password_changes_nothing()
    {
        await using TestDatabase database = await CreateWithAdminAsync();
        AccountService accounts = database.Get<AccountService>();
        string? before = await database.Get<ISessionStamp>().CurrentAsync();

        IReadOnlyList<string> problems = await accounts.ChangePasswordAsync("not-the-password-1!", "A-brand-new-Passw0rd");

        Assert.Contains("current password is not correct", Assert.Single(problems));
        Assert.NotNull(await accounts.VerifyCredentialsAsync("admin", Password));
        Assert.Equal(before, await database.Get<ISessionStamp>().CurrentAsync());
    }

    [Fact]
    public async Task The_new_password_must_differ_and_follow_the_policy()
    {
        await using TestDatabase database = await CreateWithAdminAsync();
        AccountService accounts = database.Get<AccountService>();

        Assert.Contains("different", Assert.Single(await accounts.ChangePasswordAsync(Password, Password)));
        Assert.NotEmpty(await accounts.ChangePasswordAsync(Password, "short"));
        Assert.NotNull(await accounts.VerifyCredentialsAsync("admin", Password));
    }

    [Fact]
    public async Task Changing_the_password_before_the_setup_is_explained()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();

        IReadOnlyList<string> problems = await database.Get<AccountService>().ChangePasswordAsync(Password, "A-brand-new-Passw0rd");

        Assert.Contains("setup", Assert.Single(problems));
    }
}
