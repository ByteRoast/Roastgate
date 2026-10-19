using System.Reflection;
using Microsoft.AspNetCore.Components;
using Roastgate.Core;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Security;
using Roastgate.Core.Settings;
using Roastgate.Core.Setup;
using Roastgate.Web.Services;

namespace Roastgate.Web.Components.Settings;

public partial class SettingsView
{
    [Inject] private AppSettingsService SettingsService { get; set; } = null!;
    [Inject] private HostDiagnostics Diagnostics { get; set; } = null!;
    [Inject] private AccountService Accounts { get; set; } = null!;
    [Inject] private IAdminAccountStore AccountStore { get; set; } = null!;
    [Inject] private LoginThrottle Throttle { get; set; } = null!;
    [Inject] private IAuditLog Audit { get; set; } = null!;
    [Inject] private RoastgateOptions Options { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private readonly List<string> _saveErrors = [];
    private readonly List<string> _passwordErrors = [];
    private IReadOnlyList<DiagnosticCheck> _checks = [];
    private AppSettings _original = new();
    private string _nginxRoot = string.Empty;
    private string _whitelistsDirectory = string.Empty;
    private string _acmeWebroot = string.Empty;
    private string _letsEncryptDirectory = string.Empty;
    private string? _acmeEmail;
    private CertbotMode _certbotMode;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmation = string.Empty;
    private string _username = string.Empty;
    private string _version = string.Empty;
    private bool _saved;
    private bool _isChecking;
    private bool _isChangingPassword;

    protected override async Task OnInitializedAsync()
    {
        _original = await SettingsService.GetAsync();
        _nginxRoot = _original.NginxRoot;
        _whitelistsDirectory = _original.WhitelistsDirectory;
        _acmeWebroot = _original.AcmeWebroot;
        _letsEncryptDirectory = _original.LetsEncryptDirectory;
        _acmeEmail = _original.AcmeEmail;
        _certbotMode = _original.CertbotMode;
        _username = await User.GetNameAsync();
        _version = typeof(SettingsView).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
    }

    private AppSettings Current()
    {
        return new AppSettings
        {
            NginxRoot = _nginxRoot.Trim(),
            WhitelistsDirectory = _whitelistsDirectory.Trim(),
            AcmeWebroot = _acmeWebroot.Trim(),
            LetsEncryptDirectory = _letsEncryptDirectory.Trim(),
            CertbotMode = _certbotMode,
            AcmeEmail = string.IsNullOrWhiteSpace(_acmeEmail) ? null : _acmeEmail.Trim(),
        };
    }

    private async Task SaveSettingsAsync()
    {
        _saved = false;
        _saveErrors.Clear();
        AppSettings updated = Current();
        _saveErrors.AddRange(updated.Validate());
        if (_saveErrors.Count > 0)
        {
            return;
        }

        List<string> changed = ChangedSettings(_original, updated);
        await SettingsService.SaveAsync(updated);
        await Audit.WriteAsync(await User.GetNameAsync(), "settings.update", null, true, changed.Count == 0 ? "no change" : string.Join(", ", changed));
        _original = updated;
        _saved = true;
    }

    private static List<string> ChangedSettings(AppSettings before, AppSettings after)
    {
        List<string> changed = [];
        if (before.NginxRoot != after.NginxRoot) { changed.Add("nginx directory"); }
        if (before.WhitelistsDirectory != after.WhitelistsDirectory) { changed.Add("whitelists directory"); }
        if (before.AcmeWebroot != after.AcmeWebroot) { changed.Add("ACME webroot"); }
        if (before.LetsEncryptDirectory != after.LetsEncryptDirectory) { changed.Add("Let's Encrypt directory"); }
        if (before.CertbotMode != after.CertbotMode) { changed.Add("certificate management"); }
        if (before.AcmeEmail != after.AcmeEmail) { changed.Add("Let's Encrypt e-mail"); }
        return changed;
    }

    private async Task RunDiagnosticsAsync()
    {
        _saveErrors.Clear();
        AppSettings values = Current();
        _saveErrors.AddRange(values.Validate());
        if (_saveErrors.Count > 0)
        {
            return;
        }

        _isChecking = true;
        try
        {
            _checks = await Diagnostics.RunAsync(values);
        }
        finally
        {
            _isChecking = false;
        }
    }

    private async Task ChangePasswordAsync()
    {
        _passwordErrors.Clear();
        string throttleKey = "password-change:" + _username;
        if (Throttle.IsBlocked(throttleKey, out TimeSpan retryAfter))
        {
            _passwordErrors.Add($"Too many wrong passwords. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s).");
            return;
        }

        if (_newPassword != _confirmation)
        {
            _passwordErrors.Add("The two new passwords do not match.");
            return;
        }

        _isChangingPassword = true;
        try
        {
            IReadOnlyList<string> problems = await Accounts.ChangePasswordAsync(_currentPassword, _newPassword);
            if (problems.Count > 0)
            {
                if (problems.Any((string problem) => problem.Contains("current password", StringComparison.Ordinal)))
                {
                    Throttle.RecordFailure(throttleKey);
                    await Audit.WriteAsync(_username, "auth.password-change", null, false, "wrong current password");
                }

                _passwordErrors.AddRange(problems);
                return;
            }

            Throttle.Reset(throttleKey);
            await Audit.WriteAsync(_username, "auth.password-change", null, true);

            // The security stamp changed, so this session is over as well: the sign-in page explains why.
            Navigation.NavigateTo("/login?notice=password-changed", forceLoad: true);
        }
        finally
        {
            _isChangingPassword = false;
        }
    }
}
