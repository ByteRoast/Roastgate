using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Roastgate.Web.Authentication;

namespace Roastgate.Tests.Web;

/// <summary>The config pages on the real application: protected, rendered, and reachable at their lowercase routes.</summary>
public sealed class ConfigPagesTests : IAsyncLifetime
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

    private async Task<HttpClient> SignedInBrowserAsync()
    {
        HttpClient client = _factory.CreateBrowser();
        string username = await _factory.CompleteSetupAsync();
        string ticket = _factory.Services.GetRequiredService<SignInTicketService>().Issue(username);
        HttpResponseMessage response = await client.GetAsync($"/sign-in/{ticket}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    [Theory]
    [InlineData("/reverse-proxies")]
    [InlineData("/reverse-proxies/edit")]
    [InlineData("/sites")]
    [InlineData("/sites/edit")]
    [InlineData("/whitelists")]
    [InlineData("/whitelists/edit")]
    [InlineData("/whitelists/raw")]
    public async Task Config_pages_are_protected(string path)
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login?returnUrl=", response.Headers.Location!.PathAndQuery);
    }

    [Theory]
    [InlineData("/reverse-proxies", "Reverse proxies")]
    [InlineData("/sites", "Sites")]
    [InlineData("/whitelists", "Whitelists")]
    public async Task Signed_in_users_see_the_list_pages(string path, string title)
    {
        HttpClient client = await SignedInBrowserAsync();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains(title, html);
        Assert.Contains("Test nginx", html);
    }

    [Theory]
    [InlineData("/reverse-proxies/edit")]
    [InlineData("/sites/edit")]
    [InlineData("/whitelists/raw")]
    public async Task Signed_in_users_can_open_the_new_file_pages(string path)
    {
        HttpClient client = await SignedInBrowserAsync();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("File name", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Signed_in_users_can_open_the_new_whitelist_page_with_its_list_editor()
    {
        HttpClient client = await SignedInBrowserAsync();

        HttpResponseMessage response = await client.GetAsync("/whitelists/edit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("New whitelist", html);
        Assert.Contains("Allowed addresses", html);
        Assert.Contains("Add address", html);
    }

    [Fact]
    public async Task The_whitelist_list_page_explains_that_a_whitelist_needs_an_include()
    {
        HttpClient client = await SignedInBrowserAsync();

        string html = await client.GetStringAsync("/whitelists");

        Assert.Contains("until a reverse proxy or a site includes it", html);
        Assert.Contains("New whitelist", html);
    }

    [Fact]
    public async Task The_client_address_endpoint_answers_signed_in_users_with_plain_text()
    {
        HttpClient client = await SignedInBrowserAsync();

        HttpResponseMessage response = await client.GetAsync("/client-address");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task The_client_address_endpoint_is_not_available_to_anonymous_visitors()
    {
        HttpClient client = _factory.CreateBrowser();
        await _factory.CompleteSetupAsync();

        HttpResponseMessage response = await client.GetAsync("/client-address");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Opening_a_whitelist_that_does_not_exist_says_so()
    {
        HttpClient client = await SignedInBrowserAsync();

        string html = await client.GetStringAsync("/whitelists/edit?name=ghost.conf");

        Assert.Contains("does not exist", html);
    }

    [Fact]
    public async Task Opening_a_file_that_does_not_exist_says_so()
    {
        HttpClient client = await SignedInBrowserAsync();

        HttpResponseMessage response = await client.GetAsync("/reverse-proxies/edit?name=ghost.conf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("does not exist", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_path_traversal_in_the_name_is_treated_as_a_missing_file()
    {
        HttpClient client = await SignedInBrowserAsync();

        HttpResponseMessage response = await client.GetAsync("/reverse-proxies/edit?name=..%2F..%2Fetc%2Fpasswd");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not exist", html);
        Assert.DoesNotContain("root:", html);
    }
}
