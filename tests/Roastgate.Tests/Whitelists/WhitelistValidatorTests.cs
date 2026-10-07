using Roastgate.Core.Nginx.Whitelists;

namespace Roastgate.Tests.Whitelists;

public sealed class WhitelistValidatorTests
{
    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("10.0.0.0/8")]
    [InlineData("192.168.1.0/24")]
    [InlineData("1.2.3.4/32")]
    [InlineData("0.0.0.0/0")]
    [InlineData("2001:db8::1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("::/0")]
    [InlineData("2001:db8::/32")]
    [InlineData("2001:DB8:0:0:0:0:0:1")]
    [InlineData("::ffff:1.2.3.4")]
    [InlineData("fe80::1")]
    [InlineData("  203.0.113.7  ")]
    public void Valid_addresses_are_accepted(string address)
    {
        Assert.True(WhitelistValidator.IsValid(address), address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("256.1.1.1")]
    [InlineData("1.2.3.256")]
    [InlineData("01.2.3.4")]
    [InlineData("1.2.3.04")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("1.2.3.4/33")]
    [InlineData("1.2.3.4/-1")]
    [InlineData("1.2.3.4/")]
    [InlineData("/24")]
    [InlineData("1.2.3.4/24/1")]
    [InlineData("1.2.3.4/2a")]
    [InlineData("1.2.3.4/ 24")]
    [InlineData("2001:db8::/129")]
    [InlineData("2001:db8::g")]
    [InlineData("fe80::1%eth0")]
    [InlineData("localhost")]
    [InlineData("example.com")]
    [InlineData("all")]
    [InlineData("unix:")]
    [InlineData("1.2.3.4;")]
    [InlineData("1.2.3.4; deny all")]
    [InlineData("1.2.3.4 5.6.7.8")]
    public void Anything_nginx_would_not_read_as_an_address_is_rejected(string address)
    {
        Assert.False(WhitelistValidator.IsValid(address), address);
    }

    [Fact]
    public void A_null_address_is_rejected()
    {
        Assert.False(WhitelistValidator.IsValid(null));
    }

    [Fact]
    public void The_parsed_prefix_defaults_to_the_full_length()
    {
        Assert.True(WhitelistValidator.TryParse("1.2.3.4", out _, out int ipv4Prefix, out bool ipv4HasPrefix));
        Assert.Equal(32, ipv4Prefix);
        Assert.False(ipv4HasPrefix);

        Assert.True(WhitelistValidator.TryParse("2001:db8::1", out _, out int ipv6Prefix, out _));
        Assert.Equal(128, ipv6Prefix);

        Assert.True(WhitelistValidator.TryParse("10.0.0.0/8", out _, out int prefix, out bool hasPrefix));
        Assert.Equal(8, prefix);
        Assert.True(hasPrefix);
    }

    [Fact]
    public void An_invalid_address_is_an_error_that_points_at_its_entry()
    {
        IReadOnlyList<WhitelistProblem> problems = WhitelistValidator.Validate(
            [new WhitelistEntry("1.2.3.4"), new WhitelistEntry("not-an-ip"), new WhitelistEntry("")]);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, (WhitelistProblem problem) => Assert.Equal(ProblemSeverity.Error, problem.Severity));
        Assert.Equal(1, problems[0].EntryIndex);
        Assert.Contains("not-an-ip", problems[0].Message);
        Assert.Equal(2, problems[1].EntryIndex);
        Assert.Contains("empty", problems[1].Message);
    }

    [Theory]
    [InlineData("10.0.0.5/24", "10.0.0.0/24")]
    [InlineData("192.168.1.1/16", "192.168.0.0/16")]
    [InlineData("2001:db8::1/32", "2001:db8::/32")]
    public void Host_bits_beyond_the_prefix_are_a_warning_that_suggests_the_network(string address, string suggestion)
    {
        IReadOnlyList<WhitelistProblem> problems = WhitelistValidator.Validate([new WhitelistEntry(address)]);

        WhitelistProblem problem = Assert.Single(problems);
        Assert.Equal(ProblemSeverity.Warning, problem.Severity);
        Assert.Contains(suggestion, problem.Message);
    }

    [Theory]
    [InlineData("10.0.0.0/24")]
    [InlineData("10.0.0.7/32")]
    [InlineData("10.0.0.7")]
    [InlineData("0.0.0.0/0")]
    [InlineData("2001:db8::/32")]
    public void Proper_networks_and_hosts_raise_nothing(string address)
    {
        Assert.Empty(WhitelistValidator.Validate([new WhitelistEntry(address)]));
    }

    [Theory]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("1.2.3.4", "1.2.3.4/32")]
    [InlineData("2001:DB8::1", "2001:db8:0:0:0:0:0:1")]
    [InlineData("10.0.0.0/8", "10.1.2.3/8")]
    public void The_same_network_listed_twice_is_a_warning_on_the_second_entry(string first, string second)
    {
        IReadOnlyList<WhitelistProblem> problems = WhitelistValidator.Validate([new WhitelistEntry(first), new WhitelistEntry(second)]);

        WhitelistProblem duplicate = Assert.Single(problems, (WhitelistProblem problem) => problem.Message.Contains("already listed"));
        Assert.Equal(ProblemSeverity.Warning, duplicate.Severity);
        Assert.Equal(1, duplicate.EntryIndex);
        Assert.Contains("line 1", duplicate.Message);
    }

    [Fact]
    public void A_comment_spanning_several_lines_is_an_error()
    {
        IReadOnlyList<WhitelistProblem> problems = WhitelistValidator.Validate([new WhitelistEntry("1.2.3.4", "first\nsecond")]);

        WhitelistProblem problem = Assert.Single(problems);
        Assert.Equal(ProblemSeverity.Error, problem.Severity);
    }
}
