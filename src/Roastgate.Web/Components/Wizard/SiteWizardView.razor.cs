using Microsoft.AspNetCore.Components;
using Roastgate.Core.Certificates;
using Roastgate.Core.Nginx;
using Roastgate.Core.Nginx.Generation;
using Roastgate.Web.Services;

namespace Roastgate.Web.Components.Wizard;

public partial class SiteWizardView
{
    [Inject] private ISiteWizardService Wizard { get; set; } = null!;
    [Inject] private IConfigStore Store { get; set; } = null!;
    [Inject] private ICertificateCatalog Catalog { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private sealed class WizardForm
    {
        public string Name { get; set; } = string.Empty;

        public string ServerNames { get; set; } = string.Empty;

        public int HttpPort { get; set; } = 80;

        public bool Ipv6 { get; set; } = true;

        public string Upstream { get; set; } = string.Empty;

        public bool Websockets { get; set; } = true;

        public int? ProxyTimeout { get; set; }

        public string RootDirectory { get; set; } = string.Empty;

        public bool SpaFallback { get; set; }

        public bool CacheStaticAssets { get; set; } = true;

        public bool EnableTls { get; set; }

        public int HttpsPort { get; set; } = 443;

        public string CertificatePath { get; set; } = string.Empty;

        public string CertificateKeyPath { get; set; } = string.Empty;

        public bool ForceHttps { get; set; } = true;

        public bool Http2 { get; set; } = true;

        public bool Hsts { get; set; }

        public string MaxBodySize { get; set; } = string.Empty;

        public bool SecurityHeaders { get; set; } = true;

        public bool BlockCommonExploits { get; set; } = true;

        public bool ServeAcmeChallenge { get; set; }

        public string ExtraServerDirectives { get; set; } = string.Empty;

        public string ExtraLocationDirectives { get; set; } = string.Empty;
    }

    [Parameter, EditorRequired]
    public ConfigArea Area { get; set; }

    [Parameter]
    public string? Name { get; set; }

    private readonly HashSet<string> _selectedWhitelists = new(StringComparer.Ordinal);
    private WizardForm _form = new();
    private IReadOnlyList<string> _availableWhitelists = [];
    private IReadOnlyList<CertificateInfo> _certificates = [];
    private IReadOnlyList<string> _importNotes = [];
    private IReadOnlyList<string> _importBlockers = [];
    private SiteKind _kind;
    private MetadataStatus _status = MetadataStatus.None;
    private WizardPreview? _preview;
    private ConfigResult? _result;
    private string _fileName = string.Empty;
    private string? _loadedName;
    private bool _loaded;
    private bool _isNew;
    private bool _notFound;
    private bool _isBusy;
    private bool _reload = true;
    private bool _showValidation;
    private int _tab;
    private int _previewedTab = -1;

    private AreaInfo Info => AreaInfo.For(Area);

    private IReadOnlyList<SpecProblem> Problems => SiteSpecValidator.Validate(ToSpec());

    private string? FirstDomain => ParseNames(_form.ServerNames).FirstOrDefault();

    private CertificateInfo? SelectedCertificate => _certificates.FirstOrDefault((CertificateInfo certificate) => certificate.FullChainPath == _form.CertificatePath);

    private List<string> UncoveredNames(CertificateInfo certificate)
    {
        return ParseNames(_form.ServerNames).Where((string name) => name != "_" && !certificate.Covers(name)).ToList();
    }

    private void SelectCertificate(string? name)
    {
        CertificateInfo? certificate = _certificates.FirstOrDefault((CertificateInfo item) => item.Name == name);
        _form.CertificatePath = certificate?.FullChainPath ?? string.Empty;
        _form.CertificateKeyPath = certificate?.PrivateKeyPath ?? string.Empty;
    }

    private string EffectiveName => string.IsNullOrWhiteSpace(_form.Name) ? FirstDomain ?? string.Empty : _form.Name.Trim();

    private string PreviewFileName => ConfigNames.Normalize(Area, EffectiveName) ?? "(no name yet)";

    protected override async Task OnParametersSetAsync()
    {
        if (_loaded && _loadedName == Name)
        {
            return;
        }

        _loaded = true;
        _loadedName = Name;
        _isNew = string.IsNullOrWhiteSpace(Name);
        _importNotes = [];
        _importBlockers = [];
        _selectedWhitelists.Clear();

        ConfigListing whitelists = await Store.ListAsync(ConfigArea.Whitelists);
        _availableWhitelists = whitelists.Entries.Select((ConfigEntry entry) => entry.Name).ToList();
        _certificates = (await Catalog.ListAsync()).Certificates.Select((CertificateEntry entry) => entry.Certificate).ToList();

        SiteSpec spec = await Wizard.NewSpecAsync(Area);
        if (!_isNew)
        {
            WizardFile? existing = await Wizard.LoadAsync(Area, Name!);
            if (existing is null)
            {
                _notFound = true;
                return;
            }

            _fileName = existing.File.Name;
            _status = existing.Metadata.Status;
            if (existing.Metadata.Spec is { } stored)
            {
                spec = stored;
            }
            else if (await Wizard.ImportAsync(Area, Name!) is { } import)
            {
                _importNotes = import.Notes;
                _importBlockers = import.Blockers;
                if (import.Spec is { } imported)
                {
                    spec = imported;
                }
            }
        }

        _kind = spec.Kind;
        FromSpec(spec);
    }

    private void FromSpec(SiteSpec spec)
    {
        _form = new WizardForm
        {
            Name = _form.Name,
            ServerNames = string.Join('\n', spec.ServerNames),
            HttpPort = spec.HttpPort,
            Ipv6 = spec.Ipv6,
            Upstream = spec.Upstream,
            Websockets = spec.Websockets,
            ProxyTimeout = spec.ProxyTimeoutSeconds,
            RootDirectory = spec.RootDirectory,
            SpaFallback = spec.SpaFallback,
            CacheStaticAssets = spec.CacheStaticAssets,
            EnableTls = spec.EnableTls,
            HttpsPort = spec.HttpsPort,
            CertificatePath = spec.CertificatePath,
            CertificateKeyPath = spec.CertificateKeyPath,
            ForceHttps = spec.ForceHttps,
            Http2 = spec.Http2,
            Hsts = spec.Hsts,
            MaxBodySize = spec.MaxBodySize ?? string.Empty,
            SecurityHeaders = spec.SecurityHeaders,
            BlockCommonExploits = spec.BlockCommonExploits,
            ServeAcmeChallenge = spec.ServeAcmeChallenge,
            ExtraServerDirectives = spec.ExtraServerDirectives ?? string.Empty,
            ExtraLocationDirectives = spec.ExtraLocationDirectives ?? string.Empty,
        };
        _selectedWhitelists.Clear();
        foreach (string whitelist in spec.Whitelists)
        {
            _selectedWhitelists.Add(whitelist);
        }
    }

    private SiteSpec ToSpec()
    {
        return new SiteSpec
        {
            Kind = _kind,
            ServerNames = ParseNames(_form.ServerNames),
            HttpPort = _form.HttpPort,
            Ipv6 = _form.Ipv6,
            Upstream = _form.Upstream.Trim(),
            Websockets = _form.Websockets,
            ProxyTimeoutSeconds = _form.ProxyTimeout,
            RootDirectory = _form.RootDirectory.Trim(),
            SpaFallback = _form.SpaFallback,
            CacheStaticAssets = _form.CacheStaticAssets,
            EnableTls = _form.EnableTls,
            HttpsPort = _form.HttpsPort,
            CertificatePath = _form.CertificatePath.Trim(),
            CertificateKeyPath = _form.CertificateKeyPath.Trim(),
            ForceHttps = _form.ForceHttps,
            Http2 = _form.Http2,
            Hsts = _form.Hsts,
            MaxBodySize = string.IsNullOrWhiteSpace(_form.MaxBodySize) ? null : _form.MaxBodySize.Trim(),
            SecurityHeaders = _form.SecurityHeaders,
            BlockCommonExploits = _form.BlockCommonExploits,
            Whitelists = _selectedWhitelists.OrderBy((string item) => item, StringComparer.Ordinal).ToList(),
            ServeAcmeChallenge = _form.ServeAcmeChallenge,
            ExtraServerDirectives = string.IsNullOrWhiteSpace(_form.ExtraServerDirectives) ? null : _form.ExtraServerDirectives,
            ExtraLocationDirectives = string.IsNullOrWhiteSpace(_form.ExtraLocationDirectives) ? null : _form.ExtraLocationDirectives,
        };
    }

    private static List<string> ParseNames(string text)
    {
        return text.Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>The message for a field, once the user has typed something in it or tried to save.</summary>
    private string? ErrorFor(string field, string value)
    {
        if (!_showValidation && string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Problems.FirstOrDefault((SpecProblem problem) => problem.Field == field)?.Message;
    }

    private void ToggleWhitelist(string name, bool on)
    {
        if (on)
        {
            _selectedWhitelists.Add(name);
        }
        else
        {
            _selectedWhitelists.Remove(name);
        }
    }

    private void FillLetsEncryptPaths()
    {
        if (FirstDomain is { } domain)
        {
            string directory = "/etc/letsencrypt/live/" + domain.TrimStart('*', '.');
            _form.CertificatePath = directory + "/fullchain.pem";
            _form.CertificateKeyPath = directory + "/privkey.pem";
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The preview tab is generated when it is opened, and again whenever the form changed since.
        if (_tab == 4 && _previewedTab != 4)
        {
            _previewedTab = 4;
            _result = null; // an old "nothing was saved" would be misleading once the form has been corrected
            _preview = await Wizard.PreviewAsync(Area, EffectiveName, ToSpec());
            StateHasChanged();
        }
        else if (_tab != 4)
        {
            _previewedTab = -1;
        }
    }

    private async Task SaveAsync()
    {
        _showValidation = true;
        string name = EffectiveName;
        if (string.IsNullOrWhiteSpace(name))
        {
            _result = new ConfigResult(false, "Enter a domain name (it also names the file) or a file name.");
            return;
        }

        _isBusy = true;
        try
        {
            string actor = await User.GetNameAsync();
            ConfigResult result = await Wizard.SaveAsync(Area, name, ToSpec(), _reload, actor);
            _result = result;
            if (!result.Succeeded)
            {
                return;
            }

            string? normalized = ConfigNames.Normalize(Area, name);
            if (normalized is null)
            {
                return;
            }

            _status = MetadataStatus.Intact;
            _fileName = normalized;
            if (_isNew)
            {
                _isNew = false;
                _loadedName = normalized;
                Navigation.NavigateTo(Info.WizardUrl(normalized), replace: true);
            }
        }
        finally
        {
            _isBusy = false;
        }
    }
}
