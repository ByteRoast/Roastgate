using Microsoft.AspNetCore.Components;
using Roastgate.Core.Security;
using Roastgate.Core.Settings;
using Roastgate.Core.Setup;
using Roastgate.Web.Authentication;
using Roastgate.Web.Theming;

namespace Roastgate.Web.Pages.Setup;

public partial class Index
{
    [Inject] private ISetupService SetupState { get; set; } = null!;
    [Inject] private LoginThrottle Throttle { get; set; } = null!;
    [Inject] private HostDiagnostics Diagnostics { get; set; } = null!;
    [Inject] private SignInTicketService Tickets { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private const int StepCount = 5;
    private const string TokenThrottleKey = "setup-token";

    private static readonly string[] StepTitles = ["Setup token", "Administrator account", "Paths and options", "Host diagnostics", "Summary"];

    private int _step = 1;
    private List<string> _errors = [];
    private IReadOnlyList<DiagnosticCheck> _checks = [];
    private bool _isChecking;
    private bool _isFinishing;

    private string _token = string.Empty;
    private string _username = "admin";
    private string _password = string.Empty;
    private string _confirmation = string.Empty;
    private string _nginxRoot = "/etc/nginx";
    private string _whitelistsDirectory = "/etc/nginx/whitelists";
    private string _acmeWebroot = "/var/www/certbot";
    private string _letsEncryptDirectory = "/etc/letsencrypt";
    private CertbotMode _certbotMode = CertbotMode.Embedded;
    private string? _acmeEmail;

    private string StepTitle => StepTitles[_step - 1];

    private AppSettings BuildSettings()
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

    private void GoTo(int step)
    {
        _errors = [];
        _step = step;
    }

    private void VerifyToken()
    {
        _errors = [];
        if (Throttle.IsBlocked(TokenThrottleKey, out TimeSpan retryAfter))
        {
            _errors.Add($"Too many wrong tokens. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s).");
            return;
        }

        if (!SetupState.VerifyToken(_token))
        {
            Throttle.RecordFailure(TokenThrottleKey);
            _errors.Add("The setup token is not valid.");
            return;
        }

        Throttle.Reset(TokenThrottleKey);
        _step = 2;
    }

    private void ContinueFromAccount()
    {
        _errors = [];
        _errors.AddRange(CredentialPolicy.ValidateUsername(_username.Trim().ToLowerInvariant()));
        _errors.AddRange(CredentialPolicy.ValidatePassword(_password, _username.Trim().ToLowerInvariant()));
        if (_password != _confirmation)
        {
            _errors.Add("The two passwords do not match.");
        }

        if (_errors.Count == 0)
        {
            _step = 3;
        }
    }

    private async Task ContinueFromPathsAsync()
    {
        _errors = [];
        _errors.AddRange(BuildSettings().Validate());
        if (_errors.Count > 0)
        {
            return;
        }

        _step = 4;
        await RunDiagnosticsAsync();
    }

    private async Task RunDiagnosticsAsync()
    {
        _isChecking = true;
        try
        {
            _checks = await Diagnostics.RunAsync(BuildSettings());
        }
        finally
        {
            _isChecking = false;
        }
    }

    private async Task FinishAsync()
    {
        _errors = [];
        _isFinishing = true;
        try
        {
            SetupResult result = await SetupState.CompleteAsync(new SetupRequest(_token, _username, _password, BuildSettings()));
            if (!result.Succeeded)
            {
                _errors.AddRange(result.Errors);
                return;
            }

            string ticket = Tickets.Issue(_username.Trim().ToLowerInvariant());
            Navigation.NavigateTo($"/sign-in/{ticket}", forceLoad: true);
        }
        finally
        {
            _isFinishing = false;
        }
    }
}
