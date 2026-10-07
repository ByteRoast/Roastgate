using Roastgate.Core.Nginx.Parsing;

namespace Roastgate.Tests.Parsing;

public sealed class NginxParserTests
{
    private static IReadOnlyList<NginxStatement> ParseOk(string text)
    {
        NginxParseResult result = NginxParser.Parse(text);
        Assert.True(result.Succeeded, result.Error);
        return result.Statements;
    }

    [Fact]
    public void Directives_and_blocks_are_read_with_their_arguments()
    {
        IReadOnlyList<NginxStatement> statements = ParseOk("server {\n    listen 80;\n    server_name a.example.com b.example.com;\n}\n");

        NginxStatement server = Assert.Single(statements);
        Assert.Equal("server", server.Name);
        Assert.True(server.IsBlock);
        Assert.Equal(2, server.Children!.Count);
        Assert.Equal(["a.example.com", "b.example.com"], server.Children[1].Arguments);
    }

    [Fact]
    public void Comments_are_ignored_whole_line_or_trailing()
    {
        IReadOnlyList<NginxStatement> statements = ParseOk("# top\nserver { # open\n    listen 80; # port\n}\n");

        Assert.Single(statements[0].Children!);
    }

    [Fact]
    public void Quoted_strings_keep_their_braces_semicolons_and_hashes()
    {
        IReadOnlyList<NginxStatement> statements = ParseOk("add_header X-Test \"a;b{c}#d\" always;");

        Assert.Equal(["X-Test", "a;b{c}#d", "always"], statements[0].Arguments);
    }

    [Fact]
    public void Variables_in_braces_do_not_open_a_block()
    {
        IReadOnlyList<NginxStatement> statements = ParseOk("set $x ${host}suffix;");

        Assert.False(statements[0].IsBlock);
        Assert.Equal("${host}suffix", statements[0].Arguments[1]);
    }

    [Fact]
    public void A_statement_text_is_dedented_and_keeps_its_relative_layout()
    {
        IReadOnlyList<NginxStatement> statements = ParseOk("server {\n    location /x {\n        return 204;\n    }\n}\n");

        NginxStatement location = statements[0].Children![0];
        Assert.Equal("location /x {\n    return 204;\n}", location.Text);
    }

    [Fact]
    public void Canonical_ignores_spacing_comments_and_quotes()
    {
        string first = ParseOk("add_header   X-A   \"b\"   always;")[0].Canonical;
        string second = ParseOk("add_header X-A b always; # note")[0].Canonical;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Line_numbers_are_one_based()
    {
        IReadOnlyList<NginxStatement> statements = ParseOk("a;\n\nb;\n");

        Assert.Equal(1, statements[0].Line);
        Assert.Equal(3, statements[1].Line);
    }

    [Theory]
    [InlineData("server {\n    listen 80;\n", "not closed")]
    [InlineData("listen 80\n", "missing")]
    [InlineData("}\n", "unexpected")]
    [InlineData("a \"open;\n", "not closed")]
    [InlineData("{ a; }", "without a name")]
    [InlineData(";", "empty statement")]
    public void Broken_text_reports_a_reason_with_a_line(string text, string expected)
    {
        NginxParseResult result = NginxParser.Parse(text);

        Assert.False(result.Succeeded);
        Assert.Contains(expected, result.Error);
        Assert.StartsWith("line ", result.Error);
        Assert.Empty(result.Statements);
    }

    [Fact]
    public void Windows_line_endings_are_accepted()
    {
        IReadOnlyList<NginxStatement> statements = ParseOk("server {\r\n    listen 80;\r\n}\r\n");

        Assert.Equal("listen", statements[0].Children![0].Name);
    }
}
