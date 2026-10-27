using System.Text;

namespace Roastgate.Core.Nginx.Parsing;

/// <summary>
/// A small reader for the nginx configuration syntax: directives ending with <c>;</c>, blocks in braces,
/// quoted strings, <c>#</c> comments and <c>${variable}</c> forms. It understands structure only,
/// not what the directives mean.
/// </summary>
public static class NginxParser
{
    public static NginxParseResult Parse(string text)
    {
        Reader reader = new(text.Replace("\r\n", "\n").Replace('\r', '\n'));
        List<NginxStatement> statements = [];

        string? error = reader.ReadStatements(statements, nested: false);

        return new NginxParseResult(error is null ? statements : [], error);
    }

    private sealed class Reader(string source)
    {
        private int _position;
        private int _line = 1;

        public string? ReadStatements(List<NginxStatement> into, bool nested)
        {
            while (true)
            {
                SkipSpaceAndComments();

                if (_position >= source.Length)
                {
                    return nested ? $"line {_line}: a block is not closed (missing \"}}\")" : null;
                }

                if (source[_position] == '}')
                {
                    if (!nested)
                    {
                        return $"line {_line}: unexpected \"}}\"";
                    }

                    _position++;

                    return null;
                }

                string? error = ReadStatement(into);

                if (error is not null)
                {
                    return error;
                }
            }
        }

        private string? ReadStatement(List<NginxStatement> into)
        {
            int start = _position;
            int startLine = _line;
            int column = ColumnOf(start);

            List<string> tokens = [];

            while (true)
            {
                SkipSpaceAndComments();

                if (_position >= source.Length)
                {
                    return $"line {startLine}: the statement \"{Shorten(tokens)}\" is not finished (missing \";\")";
                }

                char current = source[_position];

                if (current == ';')
                {
                    _position++;

                    if (tokens.Count == 0)
                    {
                        return $"line {startLine}: an empty statement";
                    }

                    into.Add(Build(tokens, null, start, startLine, column));

                    return null;
                }

                if (current == '{')
                {
                    _position++;

                    if (tokens.Count == 0)
                    {
                        return $"line {startLine}: a block without a name";
                    }

                    List<NginxStatement> children = [];

                    string? error = ReadStatements(children, nested: true);

                    if (error is not null)
                    {
                        return error;
                    }

                    into.Add(Build(tokens, children, start, startLine, column));

                    return null;
                }

                if (current == '}')
                {
                    return $"line {_line}: \"}}\" where \";\" was expected after \"{Shorten(tokens)}\"";
                }

                string? tokenError = ReadToken(out string token);

                if (tokenError is not null)
                {
                    return tokenError;
                }

                tokens.Add(token);
            }
        }

        private string? ReadToken(out string token)
        {
            char first = source[_position];

            if (first is '"' or '\'')
            {
                return ReadQuoted(first, out token);
            }

            StringBuilder builder = new();

            while (_position < source.Length)
            {
                char current = source[_position];

                if (char.IsWhiteSpace(current) || current is ';' or '{' or '}')
                {
                    break;
                }

                // ${name}: the braces belong to the variable, not to the structure.
                if (current == '$' && _position + 1 < source.Length && source[_position + 1] == '{')
                {
                    while (_position < source.Length && source[_position] != '}')
                    {
                        builder.Append(source[_position++]);
                    }

                    if (_position < source.Length)
                    {
                        builder.Append(source[_position++]);
                    }

                    continue;
                }

                if (current == '\\' && _position + 1 < source.Length)
                {
                    builder.Append(current);

                    _position++;

                    current = source[_position];
                }

                builder.Append(current);

                _position++;
            }

            token = builder.ToString();

            return null;
        }

        private string? ReadQuoted(char quote, out string token)
        {
            int startLine = _line;

            StringBuilder builder = new();

            _position++;

            while (_position < source.Length)
            {
                char current = source[_position];

                if (current == '\\' && _position + 1 < source.Length)
                {
                    char escaped = source[_position + 1];

                    builder.Append(escaped == quote ? escaped : current);

                    if (escaped != quote)
                    {
                        builder.Append(escaped);
                    }

                    _position += 2;

                    continue;
                }

                if (current == quote)
                {
                    _position++;

                    token = builder.ToString();

                    return null;
                }

                if (current == '\n')
                {
                    _line++;
                }

                builder.Append(current);

                _position++;
            }

            token = string.Empty;

            return $"line {startLine}: a quoted string is not closed";
        }

        private void SkipSpaceAndComments()
        {
            while (_position < source.Length)
            {
                char current = source[_position];

                if (current == '\n')
                {
                    _line++;
                    _position++;
                }
                else if (char.IsWhiteSpace(current))
                {
                    _position++;
                }
                else if (current == '#')
                {
                    while (_position < source.Length && source[_position] != '\n')
                    {
                        _position++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private NginxStatement Build(
            List<string> tokens,
            List<NginxStatement>? children,
            int start,
            int startLine,
            int column
        )
        {
            return new NginxStatement(
                tokens[0],
                tokens.Skip(1).ToList(),
                children,
                Dedent(source[start.._position], column),
                startLine
            );
        }

        private int ColumnOf(int index)
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;

            if (index == 0)
            {
                lineStart = 0;
            }

            return index - lineStart;
        }

        /// <summary>
        /// Removes the indentation the statement itself had from each of its following lines,
        /// so it can be placed elsewhere.
        /// </summary>
        private static string Dedent(string text, int column)
        {
            string[] lines = text.Split('\n');

            for (int index = 1; index < lines.Length; index++)
            {
                int removable = 0;

                while (
                    removable < column
                    && removable < lines[index].Length
                    && lines[index][removable] is ' ' or '\t'
                )
                {
                    removable++;
                }

                lines[index] = lines[index][removable..].TrimEnd();
            }

            lines[0] = lines[0].TrimEnd();

            return string.Join('\n', lines);
        }

        private static string Shorten(List<string> tokens)
        {
            string text = string.Join(' ', tokens);

            return text.Length <= 50 ? text : text[..47] + "...";
        }
    }
}
