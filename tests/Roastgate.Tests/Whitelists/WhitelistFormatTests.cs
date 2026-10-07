using Roastgate.Core.Nginx.Whitelists;

namespace Roastgate.Tests.Whitelists;

public sealed class WhitelistFormatTests
{
    [Fact]
    public void Plain_allow_lines_are_read_with_their_trailing_comments()
    {
        WhitelistParseResult result = WhitelistFormat.Parse("allow 203.0.113.7; # alice at home\nallow 10.0.0.0/8;\nallow 2001:db8::/32;   #   office  \n");

        Assert.Empty(result.Unsupported);
        Assert.NotNull(result.Document);
        Assert.Equal(
            [new WhitelistEntry("203.0.113.7", "alice at home"), new WhitelistEntry("10.0.0.0/8", null), new WhitelistEntry("2001:db8::/32", "office")],
            result.Document.Entries);
        Assert.Null(result.Document.Header);
    }

    [Fact]
    public void A_leading_comment_block_becomes_the_header()
    {
        WhitelistParseResult result = WhitelistFormat.Parse("# Alice\n#   addresses at home and at work\n\nallow 203.0.113.7;\n");

        Assert.NotNull(result.Document);
        Assert.Equal("Alice\naddresses at home and at work", result.Document.Header);
        Assert.Single(result.Document.Entries);
    }

    [Fact]
    public void Blank_lines_and_windows_line_endings_are_tolerated()
    {
        WhitelistParseResult result = WhitelistFormat.Parse("\r\n\r\nallow 1.2.3.4;\r\n\r\nallow 5.6.7.8; # two\r\n");

        Assert.NotNull(result.Document);
        Assert.Equal(["1.2.3.4", "5.6.7.8"], result.Document.Entries.Select((WhitelistEntry entry) => entry.Address).ToArray());
    }

    [Fact]
    public void An_empty_file_is_an_empty_list()
    {
        WhitelistParseResult result = WhitelistFormat.Parse(string.Empty);

        Assert.NotNull(result.Document);
        Assert.Empty(result.Document.Entries);
    }

    [Theory]
    [InlineData("deny all;", "not a plain \"allow\" line")]
    [InlineData("allow all;", "is not an IP address")]
    [InlineData("allow unix:;", "is not an IP address")]
    [InlineData("set $x 1;", "not a plain \"allow\" line")]
    [InlineData("allow 1.2.3.4", "not a plain \"allow\" line")]
    [InlineData("allow 1.2.3.4 5.6.7.8;", "not a plain \"allow\" line")]
    [InlineData("geo $ok { default 0; }", "not a plain \"allow\" line")]
    [InlineData("allow 1.2.3.4;\n# a note in the middle\nallow 5.6.7.8;", "a comment between entries")]
    public void Content_the_list_editor_cannot_represent_is_reported_instead_of_being_lost(string content, string reason)
    {
        WhitelistParseResult result = WhitelistFormat.Parse(content);

        Assert.Null(result.Document);
        Assert.Contains(result.Unsupported, (string message) => message.Contains(reason));
    }

    [Fact]
    public void Every_unsupported_line_is_listed_with_its_line_number()
    {
        WhitelistParseResult result = WhitelistFormat.Parse("allow 1.2.3.4;\ndeny all;\nallow 5.6.7.8;\nset $a 1;\n");

        Assert.Equal(2, result.Unsupported.Count);
        Assert.StartsWith("line 2:", result.Unsupported[0]);
        Assert.StartsWith("line 4:", result.Unsupported[1]);
    }

    [Fact]
    public void Addresses_are_kept_as_typed_even_when_invalid_so_the_editor_can_flag_them()
    {
        WhitelistParseResult result = WhitelistFormat.Parse("allow 999.1.1.1;\n");

        Assert.NotNull(result.Document);
        Assert.Equal("999.1.1.1", Assert.Single(result.Document.Entries).Address);
    }

    [Fact]
    public void Writing_produces_clean_allow_lines()
    {
        string text = WhitelistFormat.Write(new WhitelistDocument(null, [new WhitelistEntry("203.0.113.7", "alice"), new WhitelistEntry("10.0.0.0/8")]));

        Assert.Equal("allow 203.0.113.7; # alice\nallow 10.0.0.0/8;\n", text);
    }

    [Fact]
    public void Writing_puts_the_header_first_followed_by_a_blank_line()
    {
        string text = WhitelistFormat.Write(new WhitelistDocument("Alice\nhome and work", [new WhitelistEntry("203.0.113.7")]));

        Assert.Equal("# Alice\n# home and work\n\nallow 203.0.113.7;\n", text);
    }

    [Fact]
    public void A_comment_can_never_break_out_of_its_line()
    {
        string text = WhitelistFormat.Write(new WhitelistDocument(null, [new WhitelistEntry("1.2.3.4", "first\nallow 0.0.0.0/0;")]));

        Assert.Equal("allow 1.2.3.4; # first allow 0.0.0.0/0;\n", text);
        Assert.Single(text.TrimEnd().Split('\n'));
    }

    [Fact]
    public void Leading_hash_signs_in_comments_are_not_doubled()
    {
        string text = WhitelistFormat.Write(new WhitelistDocument(null, [new WhitelistEntry("1.2.3.4", "# already a comment")]));

        Assert.Equal("allow 1.2.3.4; # already a comment\n", text);
    }

    [Fact]
    public void An_empty_list_writes_an_empty_file()
    {
        Assert.Equal(string.Empty, WhitelistFormat.Write(new WhitelistDocument(null, [])));
    }

    [Theory]
    [InlineData("allow 203.0.113.7; # alice\nallow 10.0.0.0/8;\n")]
    [InlineData("# Header\n\nallow 1.2.3.4;\n")]
    [InlineData("# Two\n# lines\n\nallow 1.2.3.4; # x\nallow ::1;\n")]
    [InlineData("")]
    public void Reading_then_writing_gives_the_same_text_back(string text)
    {
        WhitelistParseResult parsed = WhitelistFormat.Parse(text);

        Assert.NotNull(parsed.Document);
        Assert.Equal(text, WhitelistFormat.Write(parsed.Document));
    }
}
