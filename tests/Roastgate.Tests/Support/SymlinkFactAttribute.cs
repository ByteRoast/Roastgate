namespace Roastgate.Tests.Support;

/// <summary>A fact that is skipped where symbolic links cannot be created (Windows without developer mode, for instance).</summary>
public sealed class SymlinkFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> Supported = new(CheckSupport);

    public SymlinkFactAttribute()
    {
        if (!Supported.Value)
        {
            Skip = "Creating symbolic links is not permitted in this environment.";
        }
    }

    private static bool CheckSupport()
    {
        string directory = Path.Combine(Path.GetTempPath(), "roastgate-symlink-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string target = Path.Combine(directory, "target");
            File.WriteAllText(target, "x");
            File.CreateSymbolicLink(Path.Combine(directory, "link"), "target");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
