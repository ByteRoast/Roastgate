using System.Text;

namespace Roastgate.Core.Nginx;

internal sealed class NginxWriter
{
    private readonly StringBuilder _builder = new();
    private int _depth;

    public void Blank()
    {
        // Never two blank lines in a row, and never one straight after an opening brace.
        string text = _builder.ToString();

        if (
            text.Length == 0 ||
            text.EndsWith("\n\n", StringComparison.Ordinal) ||
            text.EndsWith("{\n", StringComparison.Ordinal)
        )
        {
            return;
        }

        _builder.Append('\n');
    }

    public void Line(string text)
    {
        _builder.Append(' ', _depth * 4).Append(text).Append('\n');
    }

    /// <summary>
    /// Free text from the user: each line is indented to the current depth, its own relative indentation is kept.
    /// </summary>
    public void Lines(string text)
    {
        foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string trimmed = line.TrimEnd();

            if (trimmed.Length > 0)
            {
                _builder.Append(' ', _depth * 4).Append(trimmed.Replace("\t", "    ")).Append('\n');
            }
        }
    }

    public void Open(string header)
    {
        Line(header + " {");

        _depth++;
    }

    public void Close()
    {
        _depth--;

        Line("}");
    }

    public override string ToString()
    {
        return _builder.ToString();
    }
}
