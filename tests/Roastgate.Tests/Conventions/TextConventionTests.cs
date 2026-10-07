namespace Roastgate.Tests.Conventions;

/// <summary>Repository-wide text rules that the compiler cannot enforce.</summary>
public sealed class TextConventionTests
{
    // Built from the code point so that this file does not itself contain the forbidden character.
    private static readonly string EmDash = char.ConvertFromUtf32(0x2014);

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".razor", ".css", ".js", ".json", ".md", ".props", ".csproj", ".slnx", ".yml", ".yaml", ".editorconfig", ".gitignore", ".txt",
    };

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (.git) not found.");
    }

    private static IEnumerable<string> EnumerateTextFiles(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(file);
            if (TextExtensions.Contains(Path.GetExtension(file)) || TextExtensions.Contains(name))
            {
                yield return file;
            }
        }

        foreach (string subDirectory in Directory.EnumerateDirectories(directory))
        {
            if (!IgnoredDirectories.Contains(Path.GetFileName(subDirectory)))
            {
                foreach (string file in EnumerateTextFiles(subDirectory))
                {
                    yield return file;
                }
            }
        }
    }

    [Fact]
    public void No_text_file_contains_an_em_dash()
    {
        string root = FindRepositoryRoot();
        List<string> offenders = [];

        foreach (string file in EnumerateTextFiles(root))
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(EmDash, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{index + 1}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Em dashes are not allowed. Found in: " + string.Join(", ", offenders));
    }
}
