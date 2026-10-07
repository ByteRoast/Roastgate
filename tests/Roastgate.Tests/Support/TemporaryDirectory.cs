namespace Roastgate.Tests.Support;

/// <summary>
/// Cleaning up after a test must never fail the test. On Windows an antivirus or the indexer can keep a handle on a freshly written
/// file (SQLite's -wal and -shm files in particular) for a moment after it was closed, which makes <see cref="Directory.Delete(string, bool)"/>
/// throw. The directory lives in the system temp folder, so leaving it behind after a few attempts is harmless.
/// </summary>
internal static class TemporaryDirectory
{
    private const int Attempts = 20;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(100);

    public static void Delete(string path)
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                {
                    return;
                }

                Thread.Sleep(Pause);
            }
        }
    }
}
