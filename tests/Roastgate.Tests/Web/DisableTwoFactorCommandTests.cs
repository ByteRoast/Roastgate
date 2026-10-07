using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core;
using Roastgate.Core.Data;
using Roastgate.Core.Security;
using Roastgate.Tests.Support;
using Roastgate.Web.Cli;

namespace Roastgate.Tests.Web;

public sealed class DisableTwoFactorCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "roastgate-cli2fa-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _output = new();

    private RoastgateOptions Options => new() { DataDirectory = _directory, HostExecMode = HostExecMode.Direct };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        TemporaryDirectory.Delete(_directory);
    }

    private async Task<TestDatabase> CreateEnrolledAsync()
    {
        TestDatabase database = await TestDatabase.CreateAsync((RoastgateOptions options) => options.DataDirectory = _directory);
        await database.Get<IAdminAccountStore>().CreateAsync("admin", database.Get<PasswordHasher>().Hash("Correct-Horse-9"));
        ITwoFactorService service = database.Get<ITwoFactorService>();
        TwoFactorEnrollment enrollment = service.BeginEnrollment("admin");
        string code = Totp.Compute(Base32.Decode(enrollment.Secret)!, Totp.StepOf(DateTimeOffset.UtcNow));
        Assert.NotNull(await service.ConfirmEnrollmentAsync(enrollment.Secret, code));
        return database;
    }

    [Fact]
    public async Task It_turns_two_factor_off_ends_the_sessions_and_leaves_an_audit_entry()
    {
        string? stampBefore;
        await using (TestDatabase database = await CreateEnrolledAsync())
        {
            stampBefore = (await database.Get<IAdminAccountStore>().GetAsync())!.SecurityStamp;
        }

        int exitCode = await DisableTwoFactorCommand.RunAsync(Options, _output);

        Assert.Equal(0, exitCode);
        Assert.Contains("is off", _output.ToString());
        await using TestDatabase after = await TestDatabase.CreateAsync((RoastgateOptions options) => options.DataDirectory = _directory);
        AdminAccount account = (await after.Get<IAdminAccountStore>().GetAsync())!;
        Assert.Null(account.TotpSecret);
        Assert.NotEqual(stampBefore, account.SecurityStamp);
        Assert.Contains(await after.Get<IAuditLog>().ListRecentAsync(5), (AuditEntry entry) => entry.Action == "auth.two-factor-disabled" && entry.Actor == "cli");
    }

    [Fact]
    public async Task It_says_so_when_there_is_nothing_to_turn_off()
    {
        await using (TestDatabase database = await TestDatabase.CreateAsync((RoastgateOptions options) => options.DataDirectory = _directory))
        {
            await database.Get<IAdminAccountStore>().CreateAsync("admin", database.Get<PasswordHasher>().Hash("Correct-Horse-9"));
        }

        int exitCode = await DisableTwoFactorCommand.RunAsync(Options, _output);

        Assert.Equal(0, exitCode);
        Assert.Contains("not enabled", _output.ToString());
    }
}
