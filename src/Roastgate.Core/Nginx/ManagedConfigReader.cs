using Roastgate.Core.Settings;

namespace Roastgate.Core.Nginx;

public interface IManagedConfigReader
{
    /// <summary>Every reverse proxy (conf.d) and site (sites-available) that Roastgate manages.</summary>
    Task<IReadOnlyList<ManagedConfig>> ReadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class ManagedConfigReader(AppSettingsService settingsService) : IManagedConfigReader
{
    public async Task<IReadOnlyList<ManagedConfig>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        ConfigLocations locations = new(await settingsService.GetAsync(cancellationToken));
        List<ManagedConfig> configs = [];

        foreach (ConfigArea area in new[] { ConfigArea.ReverseProxies, ConfigArea.Sites })
        {
            string directory = locations.Directory(area);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(path);

                bool disabledProxy = area == ConfigArea.ReverseProxies &&
                                     fileName.EndsWith(ConfigNames.DisabledSuffix, StringComparison.Ordinal);

                string name = disabledProxy ? fileName[..^ConfigNames.DisabledSuffix.Length] : fileName;

                bool isManagedName = ConfigNames.IsValid(name)
                                     && (
                                         area == ConfigArea.Sites
                                         || name.EndsWith(ConfigNames.ConfigSuffix, StringComparison.Ordinal)
                                     );

                if (!isManagedName || new FileInfo(path).Length > ConfigNames.MaximumContentBytes)
                {
                    continue;
                }

                bool enabled = area == ConfigArea.ReverseProxies
                    ? !disabledProxy
                    : ConfigStore.IsLinkedTo(Path.Combine(locations.SitesEnabledDirectory, name), path);

                string content = await File.ReadAllTextAsync(path, cancellationToken);

                configs.Add(new ManagedConfig(area, name, enabled, content.Replace("\r\n", "\n").Split('\n')));
            }
        }

        return configs;
    }
}
