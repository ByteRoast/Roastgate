using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core.Data;
using Roastgate.Core.Security;
using Roastgate.Web.Authentication;

namespace Roastgate.Tests.Web;

/// <summary>End to end behaviour of setup, sign-in, authorization and URL normalisation on the real application.</summary>
public sealed class AccessFlowTests : IAsyncLifetime
{
    private readonly RoastgateFactory _factory = new();

    Task IAsyncLifetime.InitializeAsync()
    {
        return ((IAsyncLifetime)_factory).InitializeAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return ((IAsyncLifetime)_factory).DisposeAsync();
    }

    /// <summary>The redirect target as path and query. The cookie handler and Blazor redirect to absolute URLs, our middleware to relative ones.</summary>
    private static string? LocationOf(HttpResponseMessage response)
    {
        Uri? location = response.Headers.Location;
        if (location is null)
        {
            return null;
        }

        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    // ---- Before the setup -------------------------------------------------------------

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/audit-log")]
    [InlineData("/anything-else")]
    public async Task Before_the_setup_every_page_redirects_to_setup(string path)
    {
        HttpClient client = _factory.CreateBrowser();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/setup", LocationOf(response));
    }

    [Fact]
    public async Task The_setup_page_is_reachable_before_the_setup()
    {
        HttpClient client = _factory.CreateBrowser();

        HttpResponseMessage response = await client.GetAsync("/setup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Set up Roastgate", await response.Content.ReadAsStringAsync());
    }

    // ---- After the setup --------------------------------------------------------------

    [Fact]
    public async Task After_the_setup_the_setup_page_no_longer_exists()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage response = await client.GetAsync("/setup");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/audit-log")]
    public async Task Protected_pages_send_anonymous_visitors_to_the_login_page_and_remember_where_they_were_going(string path)
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/login?returnUrl={Uri.EscapeDataString(path)}", LocationOf(response));
    }

    [Fact]
    public async Task The_login_page_is_public()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage response = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign in", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Correct_credentials_sign_in_and_open_the_dashboard()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage login = await HtmlForm.PostLoginAsync(client, "admin", RoastgateFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", LocationOf(login));
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), (string cookie) => cookie.StartsWith("roastgate.auth=") && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));

        HttpResponseMessage dashboard = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("Dashboard", await dashboard.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Signing_in_returns_to_the_page_that_was_requested()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage login = await HtmlForm.PostLoginAsync(client, "admin", RoastgateFactory.AdminPassword, "/login?returnUrl=%2Faudit-log");

        Assert.Equal("/audit-log", LocationOf(login));
    }

    [Fact]
    public async Task A_return_url_pointing_to_another_site_is_ignored()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage login = await HtmlForm.PostLoginAsync(client, "admin", RoastgateFactory.AdminPassword, "/login?returnUrl=https%3A%2F%2Fevil.example%2F");

        Assert.Equal("/", LocationOf(login));
    }

    [Fact]
    public async Task Wrong_credentials_show_a_generic_error_and_do_not_sign_in()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage login = await HtmlForm.PostLoginAsync(client, "admin", "not-the-password-1!");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Invalid username or password.", await login.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_client_out_even_for_the_right_password()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        for (int attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            await HtmlForm.PostLoginAsync(client, "admin", "not-the-password-1!");
        }

        HttpResponseMessage locked = await HtmlForm.PostLoginAsync(client, "admin", RoastgateFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("Too many failed attempts", await locked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Sign_ins_and_failures_are_recorded_in_the_audit_log()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();
        await HtmlForm.PostLoginAsync(client, "admin", "not-the-password-1!");
        await HtmlForm.PostLoginAsync(client, "admin", RoastgateFactory.AdminPassword);

        IReadOnlyList<AuditEntry> audit = await _factory.Services.GetRequiredService<IAuditLog>().ListRecentAsync(10);

        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "auth.login" && !entry.Success);
        Assert.Contains(audit, (AuditEntry entry) => entry.Action == "auth.login" && entry.Success && entry.Actor == "admin");
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();
        await HtmlForm.PostLoginAsync(client, "admin", RoastgateFactory.AdminPassword);
        string dashboard = await client.GetStringAsync("/");

        HttpResponseMessage logout = await client.PostAsync("/logout", new FormUrlEncodedContent(HtmlForm.HiddenFields(dashboard)));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/login", LocationOf(logout));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_requires_the_antiforgery_token()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();
        await HtmlForm.PostLoginAsync(client, "admin", RoastgateFactory.AdminPassword);

        HttpResponseMessage logout = await client.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    // ---- Sign-in tickets (the setup wizard hands over to a real session) --------------

    [Fact]
    public async Task A_sign_in_ticket_opens_a_session_exactly_once()
    {
        HttpClient client = _factory.CreateBrowser();
        string username = await _factory.CompleteSetupAsync();
        string ticket = _factory.Services.GetRequiredService<SignInTicketService>().Issue(username);

        HttpResponseMessage first = await client.GetAsync($"/sign-in/{ticket}");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal("/", LocationOf(first));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);

        HttpClient otherBrowser = _factory.CreateBrowser();
        HttpResponseMessage replay = await otherBrowser.GetAsync($"/sign-in/{ticket}");
        Assert.Equal("/login", LocationOf(replay));
        Assert.Equal(HttpStatusCode.Redirect, (await otherBrowser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task A_made_up_sign_in_ticket_does_nothing()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage response = await client.GetAsync("/sign-in/" + new string('a', 64));

        Assert.Equal("/login", LocationOf(response));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    // ---- Sessions end when the credentials change --------------------------------------

    [Fact]
    public async Task Changing_the_password_ends_every_session_that_was_already_open()
    {
        HttpClient laptop = _factory.CreateBrowser();
        HttpClient stolen = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();
        await HtmlForm.PostLoginAsync(laptop, "admin", RoastgateFactory.AdminPassword);
        await HtmlForm.PostLoginAsync(stolen, "admin", RoastgateFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stolen.GetAsync("/")).StatusCode);

        IReadOnlyList<string> problems = await _factory.Services.GetRequiredService<AccountService>()
            .ChangePasswordAsync(RoastgateFactory.AdminPassword, "A-brand-new-Passw0rd");

        Assert.Empty(problems);
        Assert.Equal(HttpStatusCode.Redirect, (await laptop.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await stolen.GetAsync("/audit-log")).StatusCode);
        HttpResponseMessage signedInAgain = await HtmlForm.PostLoginAsync(laptop, "admin", "A-brand-new-Passw0rd");
        Assert.Equal(HttpStatusCode.Redirect, signedInAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task A_session_opened_after_the_change_is_not_affected_by_it()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();
        await _factory.Services.GetRequiredService<AccountService>().ChangePasswordAsync(RoastgateFactory.AdminPassword, "A-brand-new-Passw0rd");

        await HtmlForm.PostLoginAsync(client, "admin", "A-brand-new-Passw0rd");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/audit-log")).StatusCode);
    }

    [Theory]
    [InlineData("password-changed", "Your password was changed")]
    [InlineData("two-factor-changed", "Two-factor authentication was changed")]
    public async Task The_sign_in_page_explains_why_the_session_ended(string notice, string text)
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        string html = await client.GetStringAsync($"/login?notice={notice}");

        Assert.Contains(text, html);
    }

    [Fact]
    public async Task An_unknown_notice_shows_nothing()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        string html = await client.GetStringAsync("/login?notice=%3Cscript%3Ealert(1)%3C/script%3E");

        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.DoesNotContain("rg-auth-notice", html);
    }

    // ---- URL normalisation ------------------------------------------------------------

    [Theory]
    [InlineData("/Audit-Log", "/audit-log")]
    [InlineData("/AUDIT-LOG?page=2", "/audit-log?page=2")]
    [InlineData("/Setup", "/setup")]
    public async Task Page_urls_with_capitals_are_redirected_permanently_to_lowercase(string requested, string expected)
    {
        HttpClient client = _factory.CreateBrowser();

        HttpResponseMessage response = await client.GetAsync(requested);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, LocationOf(response));
    }
}
