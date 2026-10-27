using System.Text.Json;
using Roastgate.Core.Data.Interfaces;

namespace Roastgate.Core.Settings;

public sealed class AppSettingsService(ISettingsStore store)
{
    private const string Key = "app";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string? json = await store.GetAsync(Key, cancellationToken);

        if (json is null)
        {
            return new AppSettings();
        }

        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        return store.SetAsync(Key, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken);
    }
}
