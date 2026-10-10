using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core.Security;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Web;

/// <summary>Signing in with a passkey on the real application, with a software authenticator in place of the browser.</summary>
public sealed class PasskeyFlowTests : IAsyncLifetime
{
    // The test server answers on http://localhost.
    private static readonly PasskeyRelyingParty Site = new("localhost", "http://localhost");

    private readonly RoastgateFactory _factory = new();
    private readonly SoftwareAuthenticator _authenticator = new();

    Task IAsyncLifetime.InitializeAsync()
    {
        return ((IAsyncLifetime)_factory).InitializeAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return ((IAsyncLifetime)_factory).DisposeAsync();
    }

    private async Task RegisterPasskeyAsync()
    {
        await _factory.CompleteSetupAsync();
        
        IPasskeyService service = _factory.Services.GetRequiredService<IPasskeyService>();
        PasskeyChallenge challenge = await service.BeginRegistrationAsync(Site, "admin");
        
        Assert.NotNull(await service.CompleteRegistrationAsync(
            Site, challenge.CeremonyId, _authenticator.Register(challenge.OptionsJson, Site.Id, Site.Origin), "test"));
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, string page, string json, bool withToken = true)
    {
        HttpRequestMessage request = new(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        
        if (withToken)
        {
            request.Headers.Add("RequestVerificationToken", HtmlForm.HiddenFields(page)["__RequestVerificationToken"]);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Starts a ceremony and lets the authenticator answer; returns the body to post to the verify endpoint.</summary>
    private async Task<string> AnswerAsync(HttpClient client, string page, string? ticket)
    {
        HttpResponseMessage started = await PostJsonAsync(client, "/passkeys/sign-in/options", page, JsonSerializer.Serialize(new { ticket }));
        
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        
        using JsonDocument body = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        
        string options = body.RootElement.GetProperty("options").GetRawText();
        string response = _authenticator.Assert(options, Site.Id, Site.Origin);
        
        return $"{{\"ceremonyId\":\"{body.RootElement.GetProperty("ceremonyId").GetString()}\",\"ticket\":{JsonSerializer.Serialize(ticket)},\"response\":{response}}}";
    }

    private static async Task<HttpStatusCode> DashboardStatusAsync(HttpClient client)
    {
        return (await client.GetAsync("/")).StatusCode;
    }

    [Fact]
    public async Task A_passkey_signs_in_without_a_password()
    {
        await RegisterPasskeyAsync();
        
        HttpClient client = _factory.CreateBrowser();
        
        string page = await client.GetStringAsync("/login");
        
        Assert.Contains("Sign in with a passkey", page);

        HttpResponseMessage verified = await PostJsonAsync(client, "/passkeys/sign-in/verify", page, await AnswerAsync(client, page, null));

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal("/", (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirect").GetString());
        Assert.Equal(HttpStatusCode.OK, await DashboardStatusAsync(client));
    }

    [Fact]
    public async Task Once_a_passkey_exists_the_password_alone_is_not_enough()
    {
        await RegisterPasskeyAsync();
        
        HttpClient client = _factory.CreateBrowser();

        HttpResponseMessage response = await HtmlForm.PostLoginAsync(client, "admin", RoastgateFactory.AdminPassword);
        string page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Two-step verification", page);
        Assert.Contains("Use a passkey", page);
        Assert.DoesNotContain("Authentication code", page);
        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(client));
    }

    [Fact]
    public async Task The_password_then_a_passkey_completes_the_sign_in()
    {
        await RegisterPasskeyAsync();
        
        HttpClient client = _factory.CreateBrowser();
        
        string page = await (await HtmlForm.PostLoginAsync(client, "admin", RoastgateFactory.AdminPassword)).Content.ReadAsStringAsync();
        string ticket = HtmlForm.HiddenFields(page)["Code.Ticket"];

        HttpResponseMessage verified = await PostJsonAsync(client, "/passkeys/sign-in/verify", page, await AnswerAsync(client, page, ticket));

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await DashboardStatusAsync(client));

        // The ticket is used up: the same half signed-in state cannot be completed again.
        HttpResponseMessage again = await PostJsonAsync(client, "/passkeys/sign-in/options", page, JsonSerializer.Serialize(new { ticket }));
        
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task The_endpoints_refuse_a_request_without_the_antiforgery_token()
    {
        await RegisterPasskeyAsync();
        
        HttpClient client = _factory.CreateBrowser();
        
        string page = await client.GetStringAsync("/login");

        HttpResponseMessage response = await PostJsonAsync(client, "/passkeys/sign-in/options", page, "{}", withToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_answer_does_not_sign_in_and_repeated_ones_are_blocked()
    {
        await RegisterPasskeyAsync();
        
        HttpClient client = _factory.CreateBrowser();
        
        string page = await client.GetStringAsync("/login");

        for (int attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            string body = await AnswerAsync(client, page, null);
            string forged = body.Replace("\"signature\":\"", "\"signature\":\"AAAA", StringComparison.Ordinal);
        
            HttpResponseMessage refused = await PostJsonAsync(client, "/passkeys/sign-in/verify", page, forged);
            
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Redirect, await DashboardStatusAsync(client));
        
        HttpResponseMessage blocked = await PostJsonAsync(client, "/passkeys/sign-in/options", page, "{}");
        
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }
}
