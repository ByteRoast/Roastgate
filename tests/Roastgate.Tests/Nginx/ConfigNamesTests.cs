using Roastgate.Core.Nginx;

namespace Roastgate.Tests.Nginx;

public sealed class ConfigNamesTests
{
    [Theory]
    [InlineData(ConfigArea.ReverseProxies, "app", "app.conf")]
    [InlineData(ConfigArea.ReverseProxies, "app.conf", "app.conf")]
    [InlineData(ConfigArea.ReverseProxies, "  app  ", "app.conf")]
    [InlineData(ConfigArea.Whitelists, "alice", "alice.conf")]
    [InlineData(ConfigArea.Sites, "example.com", "example.com")]
    [InlineData(ConfigArea.Sites, "My_Site-2", "My_Site-2")]
    [InlineData(ConfigArea.Sites, "example.com\n", "example.com")]
    public void Valid_names_are_normalised(ConfigArea area, string input, string expected)
    {
        Assert.Equal(expected, ConfigNames.Normalize(area, input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("a..b")]
    [InlineData(".env")]
    [InlineData("x.disabled")]
    [InlineData("x.conf.tmp-roastgate")]
    [InlineData("é")]
    public void Invalid_names_are_rejected_in_every_area(string? input)
    {
        foreach (ConfigArea area in Enum.GetValues<ConfigArea>())
        {
            Assert.Null(ConfigNames.Normalize(area, input));
        }
    }

    [Fact]
    public void Names_are_limited_in_length()
    {
        Assert.NotNull(ConfigNames.Normalize(ConfigArea.Sites, new string('a', ConfigNames.MaximumLength)));
        Assert.Null(ConfigNames.Normalize(ConfigArea.Sites, new string('a', ConfigNames.MaximumLength + 1)));
    }
}
