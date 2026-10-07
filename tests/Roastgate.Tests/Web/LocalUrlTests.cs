using Roastgate.Web.Authentication;

namespace Roastgate.Tests.Web;

public sealed class LocalUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/audit-log", "/audit-log")]
    [InlineData("/sites?name=app", "/sites?name=app")]
    public void Local_paths_are_kept(string url, string expected)
    {
        Assert.Equal(expected, LocalUrl.OrDefault(url, "/fallback"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]
    [InlineData("evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/ok\r\nSet-Cookie: x=1")]
    public void Anything_that_could_leave_the_site_falls_back(string? url)
    {
        Assert.Equal("/fallback", LocalUrl.OrDefault(url, "/fallback"));
    }
}
