namespace Roastgate.Core.Nginx;

public sealed record DiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed)
{
    public bool HasChanges => Added > 0 || Removed > 0;
}
