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
    [InlineData("/whitelists/edit")]
    public async Task Signed_in_users_can_open_the_new_file_pages(string path)
    {
        HttpClient client = await SignedInBrowserAsync();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("File name", await response.Content.ReadAsStringAsync());
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
