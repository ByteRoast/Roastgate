using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;
using Roastgate.Core.Security;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Security;

public sealed class AccountServiceTests
{
    private const string Password = "Correct-Horse-9";

    private static async Task<TestDatabase> CreateWithAdminAsync()
    {
        TestDatabase database = await TestDatabase.CreateAsync();
        
        await database.Get<IAdminAccountStore>().CreateAsync("admin", database.Get<PasswordHasher>().Hash(Password));
        
        return database;
    }

    [Fact]
    public async Task Correct_credentials_return_the_account()
    {
        await using TestDatabase database = await CreateWithAdminAsync();

        AdminAccount? account = await database.Get<AccountService>().VerifyCredentialsAsync("admin", Password);

        Assert.NotNull(account);
        Assert.Equal("admin", account.Username);
    }

    [Fact]
    public async Task The_username_is_case_insensitive_and_trimmed()
    {
        await using TestDatabase database = await CreateWithAdminAsync();

        Assert.NotNull(await database.Get<AccountService>().VerifyCredentialsAsync("  ADMIN ", Password));
    }

    [Theory]
    [InlineData("admin", "wrong-password-1!")]
    [InlineData("someone", Password)]
    [InlineData("", "")]
    public async Task Wrong_credentials_return_nothing(string username, string password)
    {
        await using TestDatabase database = await CreateWithAdminAsync();

        Assert.Null(await database.Get<AccountService>().VerifyCredentialsAsync(username, password));
    }

    [Fact]
    public async Task Absurdly_long_passwords_are_refused_without_hashing_them()
    {
        await using TestDatabase database = await CreateWithAdminAsync();

        Assert.Null(await database.Get<AccountService>().VerifyCredentialsAsync("admin", new string('x', 100_000)));
    }

    [Fact]
    public async Task Nothing_verifies_before_the_administrator_exists()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();

        Assert.Null(await database.Get<AccountService>().VerifyCredentialsAsync("admin", Password));
    }

    [Fact]
    public async Task A_hash_with_too_few_iterations_is_upgraded_on_sign_in()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        
        IAdminAccountStore store = database.Get<IAdminAccountStore>();
        
        string weakHash = new PasswordHasher(10).Hash(Password);
        
        await store.CreateAsync("admin", weakHash);

        Assert.NotNull(await database.Get<AccountService>().VerifyCredentialsAsync("admin", Password));

        string upgraded = (await store.GetAsync())!.PasswordHash;
        
        Assert.NotEqual(weakHash, upgraded);
        Assert.StartsWith("pbkdf2-sha512$1000$", upgraded);
    }

    [Fact]
    public async Task Resetting_the_password_replaces_it()
    {
        await using TestDatabase database = await CreateWithAdminAsync();
        
        AccountService accounts = database.Get<AccountService>();

        IReadOnlyList<string> problems = await accounts.ResetPasswordAsync("A-brand-new-Passw0rd");

        Assert.Empty(problems);
        Assert.Null(await accounts.VerifyCredentialsAsync("admin", Password));
        Assert.NotNull(await accounts.VerifyCredentialsAsync("admin", "A-brand-new-Passw0rd"));
    }

    [Fact]
    public async Task Resetting_to_a_weak_password_is_refused_and_keeps_the_old_one()
    {
        await using TestDatabase database = await CreateWithAdminAsync();
        
        AccountService accounts = database.Get<AccountService>();
        IReadOnlyList<string> problems = await accounts.ResetPasswordAsync("short");

        Assert.NotEmpty(problems);
        Assert.NotNull(await accounts.VerifyCredentialsAsync("admin", Password));
    }

    [Fact]
    public async Task Resetting_without_an_administrator_explains_what_to_do()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();

        IReadOnlyList<string> problems = await database.Get<AccountService>().ResetPasswordAsync("A-brand-new-Passw0rd");

        Assert.Contains(problems, (string problem) => problem.Contains("setup"));
    }
}
