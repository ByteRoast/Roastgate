using Microsoft.Data.Sqlite;
using Roastgate.Core;
using Roastgate.Core.Data;
using Roastgate.Core.Security;
using Roastgate.Tests.Support;
using Roastgate.Web.Cli;

namespace Roastgate.Tests.Web;

public sealed class ResetPasswordCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "roastgate-cli-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _output = new();

    private RoastgateOptions Options => new() { DataDirectory = _directory, HostExecMode = HostExecMode.Direct };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Func<string, string> Typing(params string[] answers)
    {
        Queue<string> queue = new(answers);
        return (string _) => queue.Dequeue();
    }

    private async Task CreateAdminAsync(string password)
    {
        await using TestDatabase database = await TestDatabase.CreateAsync((RoastgateOptions options) => options.DataDirectory = _directory);
        await database.Get<IAdminAccountStore>().CreateAsync("admin", new PasswordHasher(1000).Hash(password));
    }

    private async Task<AdminAccount?> VerifyAsync(string password)
    {
        await using TestDatabase database = await TestDatabase.CreateAsync((RoastgateOptions options) => options.DataDirectory = _directory);
        return await database.Get<AccountService>().VerifyCredentialsAsync("admin", password);
    }

    [Fact]
    public async Task Without_an_administrator_it_explains_that_the_setup_comes_first()
    {
        int exitCode = await ResetPasswordCommand.RunAsync(Options, Typing(), _output);

        Assert.Equal(1, exitCode);
        Assert.Contains("/setup", _output.ToString());
    }

    [Fact]
    public async Task A_new_valid_password_replaces_the_old_one_and_is_audited()
    {
        await CreateAdminAsync("Old-Passw0rd-123");

        int exitCode = await ResetPasswordCommand.RunAsync(Options, Typing("Brand-New-Passw0rd", "Brand-New-Passw0rd"), _output);

        Assert.Equal(0, exitCode);
        Assert.Contains("Password updated.", _output.ToString());
        Assert.Null(await VerifyAsync("Old-Passw0rd-123"));
        Assert.NotNull(await VerifyAsync("Brand-New-Passw0rd"));

        await using TestDatabase database = await TestDatabase.CreateAsync((RoastgateOptions options) => options.DataDirectory = _directory);
        IReadOnlyList<AuditEntry> audit = await database.Get<IAuditLog>().ListRecentAsync(5);
        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "auth.password-reset" && entry.Actor == "cli");
    }

    [Fact]
    public async Task A_mismatching_confirmation_changes_nothing()
    {
        await CreateAdminAsync("Old-Passw0rd-123");

        int exitCode = await ResetPasswordCommand.RunAsync(Options, Typing("Brand-New-Passw0rd", "Something-Else-1234"), _output);

        Assert.Equal(1, exitCode);
        Assert.Contains("do not match", _output.ToString());
        Assert.NotNull(await VerifyAsync("Old-Passw0rd-123"));
    }

    [Fact]
    public async Task A_weak_password_is_refused_with_the_reasons_and_changes_nothing()
    {
        await CreateAdminAsync("Old-Passw0rd-123");

        int exitCode = await ResetPasswordCommand.RunAsync(Options, Typing("weak", "weak"), _output);

        Assert.Equal(1, exitCode);
        Assert.Contains("at least 12 characters", _output.ToString());
        Assert.NotNull(await VerifyAsync("Old-Passw0rd-123"));
    }
}
