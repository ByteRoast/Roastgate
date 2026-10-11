using Roastgate.Core;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;
using Roastgate.Core.Security;
using Roastgate.Core.Settings;
using Roastgate.Core.Setup;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Setup;

public sealed class SetupServiceTests
{
    private const string StrongPassword = "Correct-Horse-9";

    private static SetupRequest ValidRequest()
    {
        return new SetupRequest(TestDatabase.SetupToken, "admin", StrongPassword, new AppSettings());
    }

    private static async Task<(TestDatabase Database, ISetupService Setup)> CreateAsync()
    {
        TestDatabase database = await TestDatabase.CreateAsync();
        ISetupService setup = database.Get<ISetupService>();
        
        await setup.InitializeAsync();
        
        return (database, setup);
    }

    [Fact]
    public async Task A_fresh_instance_is_not_initialized_and_exposes_the_configured_token()
    {
        (TestDatabase database, ISetupService setup) = await CreateAsync();

        await using TestDatabase _ = database;

        Assert.False(setup.IsInitialized);
        Assert.Equal(TestDatabase.SetupToken, setup.SetupToken);
    }

    [Fact]
    public async Task Without_a_configured_token_a_random_one_is_generated()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync((RoastgateOptions options) => options.SetupToken = null);
        
        ISetupService setup = database.Get<ISetupService>();

        await setup.InitializeAsync();

        Assert.NotNull(setup.SetupToken);
        Assert.Equal(48, setup.SetupToken.Length);
    }

    [Fact]
    public async Task A_configured_token_that_is_too_short_is_refused_at_startup()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync((RoastgateOptions options) => options.SetupToken = "short");

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Get<ISetupService>().InitializeAsync());
    }

    [Fact]
    public async Task The_token_is_verified_exactly()
    {
        (TestDatabase database, ISetupService setup) = await CreateAsync();

        await using TestDatabase _ = database;

        Assert.True(setup.VerifyToken(TestDatabase.SetupToken));
        Assert.True(setup.VerifyToken("  " + TestDatabase.SetupToken + "\n"));
        Assert.False(setup.VerifyToken(TestDatabase.SetupToken + "x"));
        Assert.False(setup.VerifyToken(string.Empty));
    }

    [Fact]
    public async Task Completing_the_setup_creates_the_administrator_and_stores_the_settings()
    {
        (TestDatabase database, ISetupService setup) = await CreateAsync();
        
        await using TestDatabase _ = database;

        AppSettings settings = new() { NginxRoot = "/srv/nginx", WhitelistsDirectory = "/srv/nginx/wl", CertbotMode = CertbotMode.Host, AcmeEmail = "ops@example.com" };
        SetupResult result = await setup.CompleteAsync(new SetupRequest(TestDatabase.SetupToken, "Admin", StrongPassword, settings));

        Assert.True(result.Succeeded);
        Assert.True(setup.IsInitialized);
        Assert.Null(setup.SetupToken);
        Assert.NotNull(await database.Get<AccountService>().VerifyCredentialsAsync("admin", StrongPassword));

        AppSettings stored = await database.Get<AppSettingsService>().GetAsync();
        
        Assert.Equal("/srv/nginx", stored.NginxRoot);
        Assert.Equal(CertbotMode.Host, stored.CertbotMode);
        Assert.Equal("ops@example.com", stored.AcmeEmail);

        IReadOnlyList<AuditEntry> audit = await database.Get<IAuditLog>().ListRecentAsync(5);
        
        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "setup.completed" && entry.Actor == "admin");
    }

    [Fact]
    public async Task The_setup_can_only_run_once()
    {
        (TestDatabase database, ISetupService setup) = await CreateAsync();
        
        await using TestDatabase _ = database;
        await setup.CompleteAsync(ValidRequest());

        SetupResult second = await setup.CompleteAsync(new SetupRequest(TestDatabase.SetupToken, "intruder", "Another-Passw0rd-1", new AppSettings()));

        Assert.False(second.Succeeded);
        Assert.Null(await database.Get<AccountService>().VerifyCredentialsAsync("intruder", "Another-Passw0rd-1"));
    }

    [Fact]
    public async Task A_wrong_token_is_refused_and_nothing_is_created()
    {
        (TestDatabase database, ISetupService setup) = await CreateAsync();
        
        await using TestDatabase _ = database;

        SetupResult result = await setup.CompleteAsync(ValidRequest() with { Token = "not-the-token" });

        Assert.False(result.Succeeded);
        Assert.False(setup.IsInitialized);
        Assert.Null(await database.Get<IAdminAccountStore>().GetAsync());
    }

    [Theory]
    [InlineData("ad", StrongPassword)]
    [InlineData("admin", "weak")]
    [InlineData("admin", "admin-password-123")]
    public async Task Invalid_credentials_are_refused(string username, string password)
    {
        (TestDatabase database, ISetupService setup) = await CreateAsync();
        
        await using TestDatabase _ = database;

        SetupResult result = await setup.CompleteAsync(ValidRequest() with { Username = username, Password = password });

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        Assert.False(setup.IsInitialized);
    }

    [Fact]
    public async Task Invalid_settings_are_refused()
    {
        (TestDatabase database, ISetupService setup) = await CreateAsync();
        
        await using TestDatabase _ = database;

        SetupResult result = await setup.CompleteAsync(ValidRequest() with { Settings = new AppSettings { NginxRoot = "../etc; rm -rf /" } });

        Assert.False(result.Succeeded);
        Assert.False(setup.IsInitialized);
    }

    [Fact]
    public async Task Initialization_is_remembered_across_restarts()
    {
        (TestDatabase database, ISetupService setup) = await CreateAsync();
        
        await using TestDatabase _ = database;
        await setup.CompleteAsync(ValidRequest());

        SetupService restarted = new(
            database.Get<IAdminAccountStore>(),
            database.Get<AppSettingsService>(),
            database.Get<PasswordHasher>(),
            database.Get<IAuditLog>(),
            database.Get<RoastgateOptions>());
        await restarted.InitializeAsync();

        Assert.True(restarted.IsInitialized);
        Assert.Null(restarted.SetupToken);
    }
}
