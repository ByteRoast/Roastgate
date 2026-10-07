using Roastgate.Core.Settings;
using Roastgate.Tests.Support;

namespace Roastgate.Tests.Setup;

public sealed class AppSettingsTests
{
    [Fact]
    public void The_defaults_are_valid()
    {
        Assert.Empty(new AppSettings().Validate());
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("/etc/../passwd")]
    [InlineData("/etc/nginx; evil")]
    [InlineData("/etc/ngin x")]
    [InlineData("/etc/nginx\n")]
    [InlineData("C:\\nginx")]
    public void Unsafe_paths_are_rejected(string path)
    {
        Assert.NotEmpty(new AppSettings { NginxRoot = path }.Validate());
        Assert.NotEmpty(new AppSettings { WhitelistsDirectory = path }.Validate());
        Assert.NotEmpty(new AppSettings { AcmeWebroot = path }.Validate());
    }

    [Theory]
    [InlineData("ops@example.com", true)]
    [InlineData("a.b+c@sub.example.org", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("not-an-email", false)]
    [InlineData("two@@example.com", false)]
    [InlineData("space @example.com", false)]
    public void The_acme_email_is_validated_only_when_given(string? email, bool valid)
    {
        Assert.Equal(valid, new AppSettings { AcmeEmail = email }.Validate().Count == 0);
    }

    [Fact]
    public async Task Settings_survive_a_save_and_load_round_trip()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        AppSettingsService service = database.Get<AppSettingsService>();
        AppSettings original = new() { NginxRoot = "/srv/nginx", CertbotMode = CertbotMode.Disabled, AcmeEmail = "ops@example.com" };

        await service.SaveAsync(original);

        Assert.Equal(original, await service.GetAsync());
    }

    [Fact]
    public async Task Defaults_are_returned_when_nothing_was_saved()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();

        Assert.Equal(new AppSettings(), await database.Get<AppSettingsService>().GetAsync());
    }
}
