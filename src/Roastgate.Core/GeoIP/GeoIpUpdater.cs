using System.Globalization;
using System.IO.Compression;
using AxiosNet.Client;
using AxiosNet.Config;
using AxiosNet.Models;
using MaxMind.GeoIP2;

namespace Roastgate.Core.GeoIP;

public sealed class GeoIpUpdater(
    AxiosClient axios,
    GeoLocator geoLocator,
    RoastgateOptions options,
    TimeProvider timeProvider
)
{
    private const string UrlFormat = "https://download.db-ip.com/free/dbip-city-lite-{0:yyyy-MM}.mmdb.gz";

    // The archive is about 60 MB: the client is shared, so the timeout is set on this request only.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!options.AutoUpdateEnabled)
        {
            return;
        }

        DateOnly currentMonth = FirstDayOfMonth(timeProvider.GetUtcNow().UtcDateTime);

        // DB-IP builds each database on the 1st of its month, so the build date tells which month is loaded.
        if (geoLocator.BuildDate is { } buildDate && FirstDayOfMonth(buildDate) >= currentMonth)
        {
            return;
        }

        string tmpPath = options.GeoIpDatabasePath + ".tmp";

        try
        {
            // Until DB-IP publishes it, the current month answers 404. Falling back to the previous month is only worth
            // it when there is no database at all; otherwise the next pass will try again.
            bool downloaded = await DownloadAsync(currentMonth, tmpPath, cancellationToken)
                              || (
                                  !geoLocator.IsAvailable &&
                                  await DownloadAsync(currentMonth.AddMonths(-1), tmpPath, cancellationToken)
                              );

            if (!downloaded)
            {
                return;
            }

            using (new DatabaseReader(tmpPath))
            {
            }

            geoLocator.Replace(tmpPath);
        }
        catch
        {
            File.Delete(tmpPath);
            throw;
        }
    }

    private async Task<bool> DownloadAsync(DateOnly month, string destination, CancellationToken cancellationToken)
    {
        string url = string.Format(CultureInfo.InvariantCulture, UrlFormat, month);

        AxiosResponse<Stream> response = await axios.Get<Stream>(url, new AxiosRequestConfig
        {
            // A 404 is an expected answer here, not an error.
            ValidateStatus = status => status is >= 200 and < 300 or 404,
            Timeout = DownloadTimeout,
            CancellationToken = cancellationToken,
        });

        await using Stream? body = response.Data;

        if (response.Status == 404 || body is null)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using GZipStream gzip = new(body, CompressionMode.Decompress);
        await using FileStream file = File.Create(destination);

        await gzip.CopyToAsync(file, cancellationToken);

        return true;
    }

    private static DateOnly FirstDayOfMonth(DateTime date)
    {
        return new DateOnly(date.Year, date.Month, 1);
    }
}
