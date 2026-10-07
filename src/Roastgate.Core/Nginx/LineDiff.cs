namespace Roastgate.Core.Nginx;

public enum DiffKind
{
    Same,
    Added,
    Removed,
}

/// <param name="OldLine">1-based line number in the older text, null for an added line.</param>
/// <param name="NewLine">1-based line number in the newer text, null for a removed line.</param>
public sealed record DiffLine(DiffKind Kind, string Text, int? OldLine, int? NewLine);

public sealed record DiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed)
{
    public bool HasChanges => Added > 0 || Removed > 0;
}

/// <summary>A line by line comparison of two texts (longest common subsequence), for showing what a restore would change.</summary>
public static class LineDiff
{
    /// <summary>Above this many lines the quadratic algorithm is not worth it, and null is returned.</summary>
    public const int MaximumLines = 4000;

    public static DiffResult? Compute(string older, string newer)
    {
        string[] before = SplitLines(older);
        string[] after = SplitLines(newer);
        if (before.Length > MaximumLines || after.Length > MaximumLines)
        {
            return null;
        }

        // lengths[i, j]: length of the longest common subsequence of before[i..] and after[j..].
        int[,] lengths = new int[before.Length + 1, after.Length + 1];
        for (int i = before.Length - 1; i >= 0; i--)
        {
            for (int j = after.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = before[i] == after[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        List<DiffLine> lines = [];
        int added = 0;
        int removed = 0;
        int oldIndex = 0;
        int newIndex = 0;
        while (oldIndex < before.Length || newIndex < after.Length)
        {
            if (oldIndex < before.Length && newIndex < after.Length && before[oldIndex] == after[newIndex])
            {
                lines.Add(new DiffLine(DiffKind.Same, before[oldIndex], oldIndex + 1, newIndex + 1));
                oldIndex++;
                newIndex++;
            }
            else if (newIndex >= after.Length || (oldIndex < before.Length && lengths[oldIndex + 1, newIndex] >= lengths[oldIndex, newIndex + 1]))
            {
                lines.Add(new DiffLine(DiffKind.Removed, before[oldIndex], oldIndex + 1, null));
                oldIndex++;
                removed++;
            }
            else
            {
                lines.Add(new DiffLine(DiffKind.Added, after[newIndex], null, newIndex + 1));
                newIndex++;
                added++;
            }
        }

        return new DiffResult(lines, added, removed);
    }

    private static string[] SplitLines(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalized.Length == 0)
        {
            return [];
        }

        // A final line break ends the last line; it does not start an empty one.
        return (normalized.EndsWith('\n') ? normalized[..^1] : normalized).Split('\n');
    }
}
