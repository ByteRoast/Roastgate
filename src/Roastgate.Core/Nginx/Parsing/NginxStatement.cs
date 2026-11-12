namespace Roastgate.Core.Nginx.Parsing;

/// <summary>One nginx statement: a directive (<c>name args;</c>) or a block (<c>name args { ... }</c>).</summary>
/// <param name="Children">The statements inside a block, or null for a plain directive.</param>
/// <param name="Text">
/// The statement as written in the source (from its first token to its <c>;</c> or <c>}</c>),
/// without the indentation it had.
/// </param>
/// <param name="Line">1-based line where it starts.</param>
public sealed record NginxStatement(
    string Name,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<NginxStatement>? Children,
    string Text,
    int Line
)
{
    public bool IsBlock => Children is not null;

    /// <summary>
    /// A normalised form that ignores spacing, comments and quoting: two statements that mean the same have the same
    /// canonical text.
    /// </summary>
    public string Canonical
    {
        get
        {
            string head = Arguments.Count == 0 ? Name : Name + " " + string.Join(' ', Arguments);

            return IsBlock
                ? head + " { " + string.Join(' ', Children!.Select((NginxStatement child) => child.Canonical)) + " }"
                : head + ";";
        }
    }
}
