using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Roastgate.Core.Data;
using Roastgate.Core.Security;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Security;

public sealed class TwoFactorServiceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private sealed record Setup(TestDatabase Database, FakeTimeProvider Time, ITwoFactorService Service, string Secret);

    private static async Task<Setup> CreateAsync(bool enrolled = true)
    {
        FakeTimeProvider time = new(Start);
        TestDatabase database = await TestDatabase.CreateAsync(configureServices: (IServiceCollection services) => services.AddSingleton<TimeProvider>(time));
        await database.Get<IAdminAccountStore>().CreateAsync("admin", database.Get<PasswordHasher>().Hash("Correct-Horse-9"));
        ITwoFactorService service = database.Get<ITwoFactorService>();
        TwoFactorEnrollment enrollment = service.BeginEnrollment("admin");
        if (enrolled)
        {
            Assert.NotNull(await service.ConfirmEnrollmentAsync(enrollment.Secret, CodeAt(enrollment.Secret, Start, 0)));
        }

        return new Setup(database, time, service, enrollment.Secret);
    }

    private static string CodeAt(string secret, DateTimeOffset moment, int stepOffset)
    {
        return Totp.Compute(Base32.Decode(secret)!, Totp.StepOf(moment) + stepOffset);
    }

    // ---- Enrolling -------------------------------------------------------------------

    [Fact]
    public async Task Nothing_is_stored_until_a_valid_code_proves_the_app_is_set_up()
    {
        Setup setup = await CreateAsync(enrolled: false);
        await using TestDatabase database = setup.Database;

        TwoFactorEnrollment enrollment = setup.Service.BeginEnrollment("admin");

        Assert.False(await setup.Service.IsEnabledAsync());
        Assert.Null(await setup.Service.ConfirmEnrollmentAsync(enrollment.Secret, "000000"));
        Assert.False(await setup.Service.IsEnabledAsync());
    }

    [Fact]
    public async Task A_valid_code_enables_two_factor_and_returns_ten_recovery_codes_once()
    {
        Setup setup = await CreateAsync(enrolled: false);
        await using TestDatabase database = setup.Database;
        TwoFactorEnrollment enrollment = setup.Service.BeginEnrollment("admin");

        IReadOnlyList<string>? codes = await setup.Service.ConfirmEnrollmentAsync(enrollment.Secret, CodeAt(enrollment.Secret, Start, 0));

        Assert.NotNull(codes);
        Assert.Equal(RecoveryCodes.Count, codes.Count);
        Assert.True(await setup.Service.IsEnabledAsync());
        Assert.Equal(RecoveryCodes.Count, await setup.Service.RemainingRecoveryCodesAsync());
        AdminAccount account = (await database.Get<IAdminAccountStore>().GetAsync())!;
        Assert.Equal(enrollment.Secret, account.TotpSecret);
        Assert.DoesNotContain(codes[0].Replace("-", string.Empty), account.RecoveryCodeHashes!);
    }

    [Fact]
    public async Task The_code_that_confirmed_the_enrollment_cannot_be_used_to_sign_in_right_after()
    {
        Setup setup = await CreateAsync();
        await using TestDatabase database = setup.Database;

        Assert.False(await setup.Service.VerifyAsync(CodeAt(setup.Secret, Start, 0)));
    }

    [Fact]
    public async Task Enrolling_again_while_it_is_enabled_is_refused_so_the_secret_cannot_be_swapped()
    {
        Setup setup = await CreateAsync();
        await using TestDatabase database = setup.Database;
        TwoFactorEnrollment other = setup.Service.BeginEnrollment("admin");

        Assert.Null(await setup.Service.ConfirmEnrollmentAsync(other.Secret, CodeAt(other.Secret, Start, 0)));
        Assert.Equal(setup.Secret, (await database.Get<IAdminAccountStore>().GetAsync())!.TotpSecret);
    }

    [Fact]
    public async Task Enabling_two_factor_ends_the_sessions_that_were_open()
    {
        Setup setup = await CreateAsync(enrolled: false);
        await using TestDatabase database = setup.Database;
        string? before = await database.Get<ISessionStamp>().CurrentAsync();
        TwoFactorEnrollment enrollment = setup.Service.BeginEnrollment("admin");

        await setup.Service.ConfirmEnrollmentAsync(enrollment.Secret, CodeAt(enrollment.Secret, Start, 0));

        Assert.NotEqual(before, await database.Get<ISessionStamp>().CurrentAsync());
    }

    [Fact]
    public async Task The_provisioning_uri_names_the_issuer_and_the_account()
    {
        Setup setup = await CreateAsync(enrolled: false);
        await using TestDatabase database = setup.Database;

        TwoFactorEnrollment enrollment = setup.Service.BeginEnrollment("admin");

        Assert.StartsWith("otpauth://totp/Roastgate:admin?secret=" + enrollment.Secret, enrollment.ProvisioningUri);
    }

    // ---- Signing in ------------------------------------------------------------------

    [Fact]
    public async Task The_current_code_signs_in_once_and_only_once()
    {
        Setup setup = await CreateAsync();
        await using TestDatabase database = setup.Database;
        setup.Time.Advance(TimeSpan.FromSeconds(60));
        string code = CodeAt(setup.Secret, setup.Time.GetUtcNow(), 0);

        Assert.True(await setup.Service.VerifyAsync(code));
        Assert.False(await setup.Service.VerifyAsync(code));
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_does_not_burn_the_right_one()
    {
        Setup setup = await CreateAsync();
        await using TestDatabase database = setup.Database;
        setup.Time.Advance(TimeSpan.FromSeconds(60));
        string right = CodeAt(setup.Secret, setup.Time.GetUtcNow(), 0);
        string wrong = right == "000000" ? "000001" : "000000";

        Assert.False(await setup.Service.VerifyAsync(wrong));
        Assert.True(await setup.Service.VerifyAsync(right));
    }

    [Fact]
    public async Task A_code_one_step_old_is_fine_unless_a_newer_one_was_already_used()
    {
        Setup setup = await CreateAsync();
        await using TestDatabase database = setup.Database;
        setup.Time.Advance(TimeSpan.FromSeconds(90));
        DateTimeOffset now = setup.Time.GetUtcNow();

        Assert.True(await setup.Service.VerifyAsync(CodeAt(setup.Secret, now, 0)));
        Assert.False(await setup.Service.VerifyAsync(CodeAt(setup.Secret, now, -1)));
    }

    [Fact]
    public async Task Two_simultaneous_logins_with_the_same_code_cannot_both_succeed()
    {
        Setup setup = await CreateAsync();
        await using TestDatabase database = setup.Database;
        setup.Time.Advance(TimeSpan.FromSeconds(60));
        string code = CodeAt(setup.Secret, setup.Time.GetUtcNow(), 0);

        bool[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select((int _) => setup.Service.VerifyAsync(code)));

        Assert.Single(results, true);
    }

    [Fact]
    public async Task Nothing_verifies_when_two_factor_is_not_enabled()
    {
        Setup setup = await CreateAsync(enrolled: false);
        await using TestDatabase database = setup.Database;

        Assert.False(await setup.Service.VerifyAsync("123456"));
        Assert.False(await setup.Service.VerifyAsync("abcd-efgh-ijkl-mnop"));
    }

    // ---- Recovery codes --------------------------------------------------------------

    [Fact]
    public async Task A_recovery_code_signs_in_once_whatever_its_case_and_separators()
    {
        Setup setup = await CreateAsync(enrolled: false);
        await using TestDatabase database = setup.Database;
        TwoFactorEnrollment enrollment = setup.Service.BeginEnrollment("admin");
        IReadOnlyList<string> codes = (await setup.Service.ConfirmEnrollmentAsync(enrollment.Secret, CodeAt(enrollment.Secret, Start, 0)))!;

        Assert.True(await setup.Service.VerifyAsync(codes[0].ToUpperInvariant().Replace("-", " ")));
        Assert.False(await setup.Service.VerifyAsync(codes[0]));
        Assert.True(await setup.Service.VerifyAsync(codes[1]));
        Assert.Equal(RecoveryCodes.Count - 2, await setup.Service.RemainingRecoveryCodesAsync());
    }

    [Fact]
    public async Task A_code_that_looks_right_but_was_never_issued_is_refused()
    {
        Setup setup = await CreateAsync();
        await using TestDatabase database = setup.Database;

        Assert.False(await setup.Service.VerifyAsync("aaaa-bbbb-cccc-dddd"));
    }

    [Fact]
    public async Task Regenerating_replaces_every_old_recovery_code()
    {
        Setup setup = await CreateAsync(enrolled: false);
        await using TestDatabase database = setup.Database;
        TwoFactorEnrollment enrollment = setup.Service.BeginEnrollment("admin");
        IReadOnlyList<string> oldCodes = (await setup.Service.ConfirmEnrollmentAsync(enrollment.Secret, CodeAt(enrollment.Secret, Start, 0)))!;

        IReadOnlyList<string> newCodes = await setup.Service.RegenerateRecoveryCodesAsync();

        Assert.Equal(RecoveryCodes.Count, newCodes.Count);
        Assert.False(await setup.Service.VerifyAsync(oldCodes[0]));
        Assert.True(await setup.Service.VerifyAsync(newCodes[0]));
    }

    // ---- Disabling -------------------------------------------------------------------

    [Fact]
    public async Task Disabling_forgets_everything_and_ends_the_open_sessions()
    {
        Setup setup = await CreateAsync();
        await using TestDatabase database = setup.Database;
        string? before = await database.Get<ISessionStamp>().CurrentAsync();

        await setup.Service.DisableAsync();

        Assert.False(await setup.Service.IsEnabledAsync());
        AdminAccount account = (await database.Get<IAdminAccountStore>().GetAsync())!;
        Assert.Null(account.TotpSecret);
        Assert.Null(account.RecoveryCodeHashes);
        Assert.Null(account.TotpLastStep);
        Assert.NotEqual(before, await database.Get<ISessionStamp>().CurrentAsync());
        Assert.False(await setup.Service.VerifyAsync(CodeAt(setup.Secret, setup.Time.GetUtcNow(), 1)));
    }

    // ---- Challenges ------------------------------------------------------------------

    [Fact]
    public void A_challenge_ticket_identifies_the_user_until_it_is_completed()
    {
        TwoFactorChallenges challenges = new(new FakeTimeProvider(Start));
        string ticket = challenges.Issue("admin");

        Assert.Equal("admin", challenges.Peek(ticket));
        challenges.Complete(ticket);
        Assert.Null(challenges.Peek(ticket));
    }

    [Fact]
    public void A_challenge_ticket_expires()
    {
        FakeTimeProvider time = new(Start);
        TwoFactorChallenges challenges = new(time);
        string ticket = challenges.Issue("admin");

        time.Advance(TwoFactorChallenges.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(challenges.Peek(ticket));
    }

    [Fact]
    public void A_challenge_allows_five_wrong_codes_and_then_disappears()
    {
        TwoFactorChallenges challenges = new(new FakeTimeProvider(Start));
        string ticket = challenges.Issue("admin");

        for (int failure = 1; failure < TwoFactorChallenges.MaximumAttempts; failure++)
        {
            Assert.True(challenges.RecordFailure(ticket));
        }

        Assert.False(challenges.RecordFailure(ticket));
        Assert.Null(challenges.Peek(ticket));
    }

    [Fact]
    public void Unknown_tickets_are_harmless()
    {
        TwoFactorChallenges challenges = new(new FakeTimeProvider(Start));

        Assert.Null(challenges.Peek("nope"));
        Assert.False(challenges.RecordFailure("nope"));
        challenges.Complete("nope");
    }

    [Fact]
    public void Tickets_are_long_unique_and_hexadecimal()
    {
        TwoFactorChallenges challenges = new(new FakeTimeProvider(Start));

        string first = challenges.Issue("admin");
        string second = challenges.Issue("admin");

        Assert.NotEqual(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }
}
