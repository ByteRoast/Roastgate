using Roastgate.Core.Hosting;
using Roastgate.Core.Nginx.Modules;
using Roastgate.Core.Nginx.Parsing;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Nginx.Generation;

public interface ISiteWizardService
{
    /// <summary>
    /// The spec an empty wizard starts from for an area (with the ACME webroot and defaults from the settings).
    /// </summary>
    Task<SiteSpec> NewSpecAsync(ConfigArea area, CancellationToken cancellationToken = default);

    Task<WizardPreview> PreviewAsync(ConfigArea area, string name, SiteSpec spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates, generates and saves through the usual backup, test, rollback and reload cycle.
    /// </summary>
    Task<ConfigResult> SaveAsync(ConfigArea area, string name, SiteSpec spec, bool reload, string actor,
        CancellationToken cancellationToken = default);

    Task<WizardFile?> LoadAsync(ConfigArea area, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an existing hand-written file into wizard settings. Null when the file does not exist.
    /// </summary>
    Task<SiteImportResult?> ImportAsync(ConfigArea area, string name, CancellationToken cancellationToken = default);
    
    /// <summary>The modules loaded by the host's nginx (cached for a few minutes).</summary>
    Task<NginxModules> GetModulesAsync(CancellationToken cancellationToken = default);
}

public sealed class SiteWizardService(
    IConfigStore store,
    AppSettingsService settingsService,
    NginxProbe nginxProbe
) : ISiteWizardService
{
    private static readonly TimeSpan VersionCacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ModulesCacheLifetime = TimeSpan.FromMinutes(5);

    private DateTimeOffset _versionCheckedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _modulesCheckedAt = DateTimeOffset.MinValue;
    private Version? _version;
    private NginxModules _modules = NginxModules.Unknown;

    public async Task<SiteSpec> NewSpecAsync(ConfigArea area, CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);

        return new SiteSpec
        {
            Kind = area == ConfigArea.Sites ? SiteKind.StaticSite : SiteKind.ReverseProxy,
            RootDirectory = area == ConfigArea.Sites ? "/var/www/" : string.Empty,
            ServeAcmeChallenge = settings.CertbotMode != CertbotMode.Disabled,
        };
    }

    public async Task<WizardPreview> PreviewAsync(ConfigArea area, string name, SiteSpec spec,
        CancellationToken cancellationToken = default)
    {
        string? fileName = ConfigNames.Normalize(area, name);

        if (area == ConfigArea.Whitelists || fileName is null)
        {
            return new WizardPreview(
                [new SpecProblem("Name", "Enter a valid file name.")], 
                null
            );
        }

        List<SpecProblem> problems = SiteSpecValidator.Validate(spec).ToList();

        foreach (string whitelist in spec.Whitelists)
        {
            if (
                ConfigNames.Normalize(ConfigArea.Whitelists, whitelist) == whitelist
                && await store.ReadAsync(ConfigArea.Whitelists, whitelist, cancellationToken) is null
            )
            {
                problems.Add(
                    new SpecProblem(
                        nameof(SiteSpec.Whitelists),
                        $"The whitelist \"{whitelist}\" does not exist."
                    )
                );
            }
        }

        if (spec.FancyIndex is not null)
        {
            NginxModules modules = await GetModulesAsync(cancellationToken);
            
            if (modules.Known && !KnownModule.FancyIndex.IsLoaded(modules))
            {
                problems.Add(
                    new SpecProblem(
                        nameof(SiteSpec.FancyIndex),
                        "The fancyindex module is not loaded in nginx. Install libnginx-mod-http-fancyindex and reload nginx."
                    )
                );
            }
        }

        if (problems.Count > 0)
        {
            return new WizardPreview(problems, null);
        }

        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        Version? version = await GetNginxVersionAsync(cancellationToken);
        GenerationContext context = new(new ConfigLocations(settings), settings.AcmeWebroot, fileName, version);

        string body = SiteGenerator.Generate(spec, context);

        return new WizardPreview([], SiteMetadataFormat.Compose(spec, body), version?.ToString());
    }

    /// <summary>The host's nginx version (asked at most every few minutes), or null when nginx cannot be run.</summary>
    private async Task<Version?> GetNginxVersionAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - _versionCheckedAt < VersionCacheLifetime)
        {
            return _version;
        }

        NginxProbeResult probe = await nginxProbe.ProbeAsync(cancellationToken);

        _version = probe.Version is { } text && Version.TryParse(text, out Version? parsed) ? parsed : null;
        _versionCheckedAt = DateTimeOffset.UtcNow;

        return _version;
    }

    public async Task<NginxModules> GetModulesAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - _modulesCheckedAt < ModulesCacheLifetime)
        {
            return _modules;
        }

        _modules = await nginxProbe.ProbeModulesAsync(cancellationToken);
        _modulesCheckedAt = DateTimeOffset.UtcNow;

        return _modules;
    }

    public async Task<ConfigResult> SaveAsync(
        ConfigArea area,
        string name,
        SiteSpec spec,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default
    )
    {
        WizardPreview preview = await PreviewAsync(area, name, spec, cancellationToken);

        if (preview.Text is null)
        {
            string message = "Nothing was saved: " +
                             string.Join(" ", preview.Problems.Select((SpecProblem problem) => problem.Message));

            return new ConfigResult(false, message);
        }

        return await store.SaveAsync(area, name, preview.Text, reload, actor, cancellationToken);
    }

    public async Task<WizardFile?> LoadAsync(
        ConfigArea area,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        ConfigFile? file = await store.ReadAsync(area, name, cancellationToken);

        return file is null
            ? null
            : new WizardFile(file, SiteMetadataFormat.Read(file.Content));
    }

    public async Task<SiteImportResult?> ImportAsync(
        ConfigArea area,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        ConfigFile? file = area == ConfigArea.Whitelists ? null : await store.ReadAsync(area, name, cancellationToken);

        if (file is null)
        {
            return null;
        }

        AppSettings settings = await settingsService.GetAsync(cancellationToken);

        return SiteImporter.Import(file.Content, new ConfigLocations(settings), settings.AcmeWebroot);
    }
}
