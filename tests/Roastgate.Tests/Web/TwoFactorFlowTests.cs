using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;
using Roastgate.Core.Security;

namespace Roastgate.Tests.Web;

/// <summary>The two-step sign-in on the real application, with a clock the tests control.</summary>
public sealed class TwoFactorFlowTests : IAsyncLifetime
{
    private static readonly TimeSpan OneStep = TimeSpan.FromSeconds(Totp.StepSeconds);

    private readonly RoastgateFactory _factory = new();

    Task IAsyncLifetime.InitializeAsync()
    {
        return ((IAsyncLifetime)_factory).InitializeAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return ((IAsyncLifetime)_factory).DisposeAsync();
    }

    private sealed record Enrolled(string Secret, IReadOnlyList<string> RecoveryCodes);

    /// <summary>Sets up the instance and turns two-factor on. The clock is then moved on so that fresh codes are available.</summary>
    private async Task<Enrolled> EnableAsync()
    {
        await _factory.CompleteSetupAsync();
        
        ITwoFactorService service = _factory.Services.GetRequiredService<ITwoFactorService>();
        TwoFactorEnrollment enrollment = service.BeginEnrollment("admin");
        IReadOnlyList<string>? codes = await service.ConfirmEnrollmentAsync(enrollment.Secret, CodeFor(enrollment.Secret));
        
        Assert.NotNull(codes);
        
        _factory.Time.Advance(OneStep);
        
        return new Enrolled(enrollment.Secret, codes);
    }

    private string CodeFor(string secret, int stepOffset = 0)
    {
        return Totp.Compute(Base32.Decode(secret)!, Totp.StepOf(_factory.Time.GetUtcNow()) + stepOffset);
    }

    private static async Task<(string Page, HttpResponseMessage Response)> PasswordStepAsync(HttpClient client, string password = RoastgateFactory.AdminPassword)
    {
        HttpResponseMessage response = await HtmlForm.PostLoginAsync(client, "admin", password);
     
        return (await response.Content.ReadAsStringAsync(), response);
    }

    private static Task<HttpResponseMessage> CodeStepAsync(HttpClient client, string page, string code)
    {
        return HtmlForm.PostAsync(client, "/login", page, new Dictionary<string, string> { ["Code.Code"] = code });
    }

    private static async Task<HttpStatusCode> DashboardStatusAsync(HttpClient client)
    {
        return (await client.GetAsync("/")).StatusCode;
    }

    // ---- The two steps ---------------------------------------------------------------

    [Fact]
    public async Task A_correct_password_alone_is_not_enough_once_two_factor_is_on()
    {
        HttpClient client = _factory.CreateBrowser();
        
        await EnableAsync();

        (string page, HttpResponseMessage response) = await PasswordStepAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Two-step verification", page);
        Assert.Contains("Authentication code", page);
        Assert.DoesNotContain(response.Headers, (KeyValuePair<string, IEnumerable<string>> header) =>
            header.Key == "Set-Cookie" && header.Value.Any((string value) => value.StartsWith("roastgate.auth=")));
        
        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(client));
    }

    [Fact]
    public async Task The_current_code_completes_the_sign_in()
    {
        HttpClient client = _factory.CreateBrowser();
        Enrolled enrolled = await EnableAsync();
        
        (string page, _) = await PasswordStepAsync(client);

        HttpResponseMessage response = await CodeStepAsync(client, page, CodeFor(enrolled.Secret));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.IsAbsoluteUri ? response.Headers.Location.PathAndQuery : response.Headers.Location.OriginalString);
        Assert.Equal(HttpStatusCode.OK, await DashboardStatusAsync(client));
    }

    [Fact]
    public async Task A_wrong_code_does_not_sign_in_and_the_right_one_still_works_afterwards()
    {
        HttpClient client = _factory.CreateBrowser();
        Enrolled enrolled = await EnableAsync();
        
        (string page, _) = await PasswordStepAsync(client);
        string right = CodeFor(enrolled.Secret);
        string wrong = right == "000000" ? "111111" : "000000";

        HttpResponseMessage failure = await CodeStepAsync(client, page, wrong);
        
        string failurePage = await failure.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, failure.StatusCode);
        Assert.Contains("That code is not valid", failurePage);
        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(client));

        HttpResponseMessage success = await CodeStepAsync(client, failurePage, right);
        
        Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await DashboardStatusAsync(client));
    }

    [Fact]
    public async Task A_code_that_was_just_used_cannot_sign_anybody_in_again()
    {
        Enrolled enrolled = await EnableAsync();
        
        string code = CodeFor(enrolled.Secret);
        
        HttpClient first = _factory.CreateBrowser();
        
        (string firstPage, _) = await PasswordStepAsync(first);
        
        Assert.Equal(HttpStatusCode.Redirect, (await CodeStepAsync(first, firstPage, code)).StatusCode);

        HttpClient attacker = _factory.CreateBrowser();
        
        (string attackerPage, _) = await PasswordStepAsync(attacker);
        
        HttpResponseMessage replay = await CodeStepAsync(attacker, attackerPage, code);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Contains("That code is not valid", await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(attacker));
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once()
    {
        Enrolled enrolled = await EnableAsync();
        HttpClient first = _factory.CreateBrowser();
        
        (string firstPage, _) = await PasswordStepAsync(first);

        HttpResponseMessage success = await CodeStepAsync(first, firstPage, enrolled.RecoveryCodes[0]);
        
        Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await DashboardStatusAsync(first));

        HttpClient second = _factory.CreateBrowser();
        
        (string secondPage, _) = await PasswordStepAsync(second);
        
        HttpResponseMessage reuse = await CodeStepAsync(second, secondPage, enrolled.RecoveryCodes[0]);
        
        Assert.Contains("That code is not valid", await reuse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(second));
    }

    // ---- Brute force -----------------------------------------------------------------

    [Fact]
    public async Task Five_wrong_codes_end_the_attempt_and_the_password_has_to_be_entered_again()
    {
        HttpClient client = _factory.CreateBrowser();
        Enrolled enrolled = await EnableAsync();
        
        (string page, _) = await PasswordStepAsync(client);
        string wrong = CodeFor(enrolled.Secret) == "000000" ? "111111" : "000000";
        string lastPage = page;

        for (int attempt = 0; attempt < TwoFactorChallenges.MaximumAttempts; attempt++)
        {
            lastPage = await (await CodeStepAsync(client, lastPage.Contains("Code.Ticket") ? lastPage : page, wrong)).Content.ReadAsStringAsync();
        }

        Assert.Contains("Too many wrong codes", lastPage);
        Assert.Contains("Username", lastPage);

        // The ticket is gone: even the right code does nothing with it.
        HttpResponseMessage stale = await CodeStepAsync(client, page, CodeFor(enrolled.Secret));
        
        Assert.Contains("expired", await stale.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(client));
    }

    [Fact]
    public async Task Starting_over_with_the_password_does_not_give_a_client_a_fresh_supply_of_code_attempts()
    {
        HttpClient client = _factory.CreateBrowser();
        Enrolled enrolled = await EnableAsync();
        
        string wrong = CodeFor(enrolled.Secret) == "000000" ? "111111" : "000000";

        for (int attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            (string page, _) = await PasswordStepAsync(client);
        
            await CodeStepAsync(client, page, wrong);
        }

        // The correct code, with a correct password, is now refused: this address is locked out.
        (string finalPage, _) = await PasswordStepAsync(client);
        
        HttpResponseMessage blocked = await CodeStepAsync(client, finalPage, CodeFor(enrolled.Secret));
        
        Assert.Contains("Too many failed attempts", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(client));
    }

    [Fact]
    public async Task A_made_up_or_expired_ticket_is_refused()
    {
        HttpClient client = _factory.CreateBrowser();
        Enrolled enrolled = await EnableAsync();
        
        (string page, _) = await PasswordStepAsync(client);

        HttpResponseMessage forged = await HtmlForm.PostAsync(client, "/login", page, new Dictionary<string, string>
        {
            ["Code.Ticket"] = new string('a', 64),
            ["Code.Code"] = CodeFor(enrolled.Secret),
        });
        
        Assert.Contains("expired", await forged.Content.ReadAsStringAsync());

        _factory.Time.Advance(TwoFactorChallenges.Lifetime + TimeSpan.FromSeconds(1));
        
        HttpResponseMessage late = await CodeStepAsync(client, page, CodeFor(enrolled.Secret));
        
        Assert.Contains("expired", await late.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(client));
    }

    [Fact]
    public async Task A_wrong_password_never_reveals_that_a_second_step_exists()
    {
        HttpClient client = _factory.CreateBrowser();
        
        await EnableAsync();

        (string page, _) = await PasswordStepAsync(client, "not-the-password-1!");

        Assert.Contains("Invalid username or password.", page);
        Assert.DoesNotContain("Two-step", page);
        Assert.DoesNotContain("Authentication code", page);
    }

    // ---- Sessions and switching it off -----------------------------------------------

    [Fact]
    public async Task Turning_two_factor_on_ends_the_sessions_that_were_open()
    {
        await _factory.CompleteSetupAsync();
        
        HttpClient open = _factory.CreateBrowser();
        
        await HtmlForm.PostLoginAsync(open, "admin", RoastgateFactory.AdminPassword);
        
        Assert.Equal(HttpStatusCode.OK, await DashboardStatusAsync(open));
        
        ITwoFactorService service = _factory.Services.GetRequiredService<ITwoFactorService>();
        TwoFactorEnrollment enrollment = service.BeginEnrollment("admin");

        await service.ConfirmEnrollmentAsync(enrollment.Secret, CodeFor(enrollment.Secret));

        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(open));
    }

    [Fact]
    public async Task After_two_factor_is_turned_off_the_password_is_enough_again_and_old_sessions_stay_closed()
    {
        Enrolled enrolled = await EnableAsync();
        HttpClient old = _factory.CreateBrowser();
        
        (string page, _) = await PasswordStepAsync(old);
        
        await CodeStepAsync(old, page, CodeFor(enrolled.Secret));
        
        Assert.Equal(HttpStatusCode.OK, await DashboardStatusAsync(old));

        await _factory.Services.GetRequiredService<ITwoFactorService>().DisableAsync();

        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(old));
        
        HttpClient fresh = _factory.CreateBrowser();
        HttpResponseMessage login = await HtmlForm.PostLoginAsync(fresh, "admin", RoastgateFactory.AdminPassword);
        
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await DashboardStatusAsync(fresh));
    }

    [Fact]
    public async Task The_attempts_are_audited()
    {
        Enrolled enrolled = await EnableAsync();
        HttpClient client = _factory.CreateBrowser();
        
        (string page, _) = await PasswordStepAsync(client);
        
        await CodeStepAsync(client, page, CodeFor(enrolled.Secret) == "000000" ? "111111" : "000000");

        IReadOnlyList<AuditEntry> audit = await _factory.Services.GetRequiredService<IAuditLog>().ListRecentAsync(10);

        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "auth.two-factor" && !entry.Success);
    }
}
