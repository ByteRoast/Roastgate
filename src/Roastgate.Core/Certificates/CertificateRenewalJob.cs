using System.Text.Json;
using Roastgate.Core.Data;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Certificates;

public sealed record RenewalStatus(DateTimeOffset? LastRun, bool? LastSucceeded, string? Summary);

public interface ICertificateRenewalJob
{
    /// <summary>One renewal pass. Does nothing unless certificates are managed by the embedded certbot.</summary>
    Task RunOnceAsync(CancellationToken cancellationToken = default);

    Task<RenewalStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What the background timer runs twice a day. Only the embedded certbot is renewed here: with the host's certbot, its own
/// timer does the job, and two renewers at once is exactly what the setup diagnostics warn about.
/// </summary>
public sealed class CertificateRenewalJob(
    AppSettingsService settingsService,
    ICertificateService certificates,
    ISettingsStore settings,
    TimeProvider timeProvider) : ICertificateRenewalJob
{
    private const string StatusKey = "certbot.renewal";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        AppSettings current = await settingsService.GetAsync(cancellationToken);
        if (current.CertbotMode != CertbotMode.Embedded)
        {
            return;
        }

        CertificateOperation result = await certificates.RenewAsync(null, force: false, "system", cancellationToken);
        RenewalStatus status = new(timeProvider.GetUtcNow(), result.Succeeded, result.Message);
        await settings.SetAsync(StatusKey, JsonSerializer.Serialize(status, JsonOptions), cancellationToken);
    }

    public async Task<RenewalStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        string? json = await settings.GetAsync(StatusKey, cancellationToken);
        if (json is null)
        {
            return new RenewalStatus(null, null, null);
        }

        try
        {
            return JsonSerializer.Deserialize<RenewalStatus>(json, JsonOptions) ?? new RenewalStatus(null, null, null);
        }
        catch (JsonException)
        {
            return new RenewalStatus(null, null, null);
        }
    }
}
