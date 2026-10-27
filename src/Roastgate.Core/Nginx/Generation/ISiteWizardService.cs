using Roastgate.Core.Hosting;
using Roastgate.Core.Nginx.Parsing;

namespace Roastgate.Core.Nginx.Generation;

public interface ISiteWizardService
{
    /// <summary>
    /// The spec an empty wizard starts from for an area (with the ACME webroot and defaults from the settings).
    /// </summary>
    Task<SiteSpec> NewSpecAsync(ConfigArea area, CancellationToken cancellationToken = default);

    Task<WizardPreview> PreviewAsync(
        ConfigArea area,
        string name,
        SiteSpec spec,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Validates, generates and saves through the usual backup, test, rollback and reload cycle.
    /// </summary>
    Task<ConfigResult> SaveAsync(
        ConfigArea area,
        string name,
        SiteSpec spec,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default
    );

    Task<WizardFile?> LoadAsync(ConfigArea area, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an existing hand-written file into wizard settings. Null when the file does not exist.
    /// </summary>
    Task<SiteImportResult?> ImportAsync(ConfigArea area, string name, CancellationToken cancellationToken = default);

    /// <summary>The modules loaded by the host's nginx (cached for a few minutes).</summary>
    Task<NginxModules> GetModulesAsync(CancellationToken cancellationToken = default);
}
