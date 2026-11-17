using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace Roastgate.Core.AccessLogs;

public sealed class AccessLogReader(RoastgateOptions options)
{
    public async IAsyncEnumerable<AccessLogEntry> ReadAsync(
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(options.NginxLogDirectory))
        {
            yield break;
        }

        IEnumerable<string> files = Directory
            .EnumerateFiles(options.NginxLogDirectory, "access.log*")
            .OrderByDescending(File.GetLastWriteTimeUtc);

        foreach (string file in files)
        {
            if (File.GetLastWriteTimeUtc(file) < since.UtcDateTime)
            {
                // this file and all others are too old
                yield break;
            }

            await using FileStream fileStream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using Stream stream = file.EndsWith(".gz", StringComparison.Ordinal)
                ? new GZipStream(fileStream, CompressionMode.Decompress)
                : fileStream;

            using StreamReader streamReader = new(stream);

            while (await streamReader.ReadLineAsync(cancellationToken) is { } line)
            {
                AccessLogEntry? accessLogEntry = AccessLogParser.Parse(line);

                if (accessLogEntry == null || accessLogEntry.Time < since)
                {
                    continue;
                }

                yield return accessLogEntry;
            }
        }
    }
}
