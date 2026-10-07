using Roastgate.Core.Nginx;

namespace Roastgate.Tests.Nginx;

public sealed class LineDiffTests
{
    private static string Render(DiffResult? result)
    {
        return string.Join('|', result!.Lines.Select((DiffLine line) => (line.Kind switch { DiffKind.Added => "+", DiffKind.Removed => "-", _ => " " }) + line.Text));
    }

    [Fact]
    public void Identical_texts_have_no_changes()
    {
        DiffResult? result = LineDiff.Compute("a\nb\nc\n", "a\nb\nc\n");

        Assert.NotNull(result);
        Assert.False(result.HasChanges);
        Assert.Equal(" a| b| c", Render(result));
    }

    [Fact]
    public void An_added_line_is_marked_added_with_only_its_new_line_number()
    {
        DiffResult? result = LineDiff.Compute("a\nc\n", "a\nb\nc\n");

        Assert.Equal(" a|+b| c", Render(result));
        Assert.Equal(1, result!.Added);
        Assert.Equal(0, result.Removed);
        DiffLine added = result.Lines.Single((DiffLine line) => line.Kind == DiffKind.Added);
        Assert.Null(added.OldLine);
        Assert.Equal(2, added.NewLine);
    }

    [Fact]
    public void A_removed_line_is_marked_removed_with_only_its_old_line_number()
    {
        DiffResult? result = LineDiff.Compute("a\nb\nc\n", "a\nc\n");

        Assert.Equal(" a|-b| c", Render(result));
        DiffLine removed = result!.Lines.Single((DiffLine line) => line.Kind == DiffKind.Removed);
        Assert.Equal(2, removed.OldLine);
        Assert.Null(removed.NewLine);
    }

    [Fact]
    public void A_changed_line_is_a_removal_followed_by_an_addition()
    {
        DiffResult? result = LineDiff.Compute("listen 80;\nserver_name a;\n", "listen 8080;\nserver_name a;\n");

        Assert.Equal("-listen 80;|+listen 8080;| server_name a;", Render(result));
        Assert.Equal(1, result!.Added);
        Assert.Equal(1, result.Removed);
    }

    [Fact]
    public void Everything_is_added_to_an_empty_file_and_removed_when_the_new_text_is_empty()
    {
        Assert.Equal("+a|+b", Render(LineDiff.Compute(string.Empty, "a\nb\n")));
        Assert.Equal("-a|-b", Render(LineDiff.Compute("a\nb\n", string.Empty)));
        Assert.False(LineDiff.Compute(string.Empty, string.Empty)!.HasChanges);
    }

    [Fact]
    public void Line_endings_and_the_final_line_break_do_not_count_as_changes()
    {
        Assert.False(LineDiff.Compute("a\r\nb\r\n", "a\nb")!.HasChanges);
    }

    [Fact]
    public void Blank_lines_are_real_lines()
    {
        DiffResult? result = LineDiff.Compute("a\n\nb\n", "a\nb\n");

        Assert.Equal(" a|-| b", Render(result));
    }

    [Fact]
    public void Repeated_lines_are_matched_in_order()
    {
        DiffResult? result = LineDiff.Compute("x\nx\nx\n", "x\nx\n");

        Assert.Equal(1, result!.Removed);
        Assert.Equal(2, result.Lines.Count((DiffLine line) => line.Kind == DiffKind.Same));
    }

    [Fact]
    public void Applying_the_diff_to_the_old_text_gives_the_new_one()
    {
        string older = "server {\n    listen 80;\n    server_name a.example.com;\n    location / {\n        proxy_pass http://127.0.0.1:3000;\n    }\n}\n";
        string newer = "server {\n    listen 8080;\n    server_name a.example.com b.example.com;\n    location / {\n        proxy_pass http://127.0.0.1:3000;\n        proxy_buffering off;\n    }\n}\n";

        DiffResult result = LineDiff.Compute(older, newer)!;

        string rebuiltNew = string.Join('\n', result.Lines.Where((DiffLine line) => line.Kind != DiffKind.Removed).Select((DiffLine line) => line.Text)) + "\n";
        string rebuiltOld = string.Join('\n', result.Lines.Where((DiffLine line) => line.Kind != DiffKind.Added).Select((DiffLine line) => line.Text)) + "\n";
        Assert.Equal(newer, rebuiltNew);
        Assert.Equal(older, rebuiltOld);
    }

    [Fact]
    public void Texts_that_are_too_long_for_the_quadratic_algorithm_are_declined()
    {
        string big = string.Join('\n', Enumerable.Range(0, LineDiff.MaximumLines + 1).Select((int index) => "line " + index));

        Assert.Null(LineDiff.Compute(big, "x"));
        Assert.Null(LineDiff.Compute("x", big));
        Assert.NotNull(LineDiff.Compute(string.Join('\n', Enumerable.Range(0, 500).Select((int index) => "line " + index)), "x"));
    }
}
