using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using QRCoder;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Security;
using Roastgate.Web.Services;

namespace Roastgate.Web.Components.Settings;

public partial class TwoFactorSection
{
    [Inject] private ITwoFactorService TwoFactor { get; set; } = null!;
    [Inject] private AccountService Accounts { get; set; } = null!;
    [Inject] private LoginThrottle Throttle { get; set; } = null!;
    [Inject] private IAuditLog Audit { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private readonly List<string> _errors = [];
    private TwoFactorEnrollment? _enrollment;
    private IReadOnlyList<string>? _recoveryCodes;
    private string _qrSvg = string.Empty;
    private string _username = string.Empty;
    private string _code = string.Empty;
    private string _regenerateCode = string.Empty;
    private string _disablePassword = string.Empty;
    private string _disableCode = string.Empty;
    private bool _enabled;
    private bool _copied;
    private bool _isBusy;
    private int _remaining;
    private bool _signInAfterCodes;

    protected override async Task OnInitializedAsync()
    {
        _username = await User.GetNameAsync();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _enabled = await TwoFactor.IsEnabledAsync();
        _remaining = _enabled ? await TwoFactor.RemainingRecoveryCodesAsync() : 0;
    }

    private Task StartAsync()
    {
        _errors.Clear();
        _enrollment = TwoFactor.BeginEnrollment(_username);
        _qrSvg = BuildQrCode(_enrollment.ProvisioningUri);
        _code = string.Empty;
        return Task.CompletedTask;
    }

    private void Cancel()
    {
        _enrollment = null;
        _errors.Clear();
    }

    private async Task ConfirmAsync()
    {
        _errors.Clear();
        if (_enrollment is null)
        {
            return;
        }

        string throttleKey = "two-factor-setup:" + _username;
        if (Throttle.IsBlocked(throttleKey, out TimeSpan retryAfter))
        {
            _errors.Add($"Too many wrong codes. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s).");
            return;
        }

        _isBusy = true;
        try
        {
            IReadOnlyList<string>? codes = await TwoFactor.ConfirmEnrollmentAsync(_enrollment.Secret, _code);
            if (codes is null)
            {
                Throttle.RecordFailure(throttleKey);
                _errors.Add("That code is not valid. Check that your phone's clock is set automatically, and enter the code the app shows now.");
                return;
            }

            Throttle.Reset(throttleKey);
            await Audit.WriteAsync(_username, "auth.two-factor-enabled", null, true);
            _recoveryCodes = codes;
            _signInAfterCodes = true;
            _enrollment = null;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task RegenerateAsync()
    {
        _errors.Clear();
        string throttleKey = "two-factor-confirm:" + _username;
        if (Throttle.IsBlocked(throttleKey, out TimeSpan retryAfter))
        {
            _errors.Add($"Too many wrong codes. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s).");
            return;
        }

        _isBusy = true;
        try
        {
            if (!await TwoFactor.VerifyAsync(_regenerateCode))
            {
                Throttle.RecordFailure(throttleKey);
                _errors.Add("That code is not valid.");
                return;
            }

            Throttle.Reset(throttleKey);
            _recoveryCodes = await TwoFactor.RegenerateRecoveryCodesAsync();
            _signInAfterCodes = false;
            _regenerateCode = string.Empty;
            await Audit.WriteAsync(_username, "auth.recovery-codes-regenerated", null, true);
            await RefreshAsync();
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task DisableAsync()
    {
        _errors.Clear();
        string throttleKey = "two-factor-confirm:" + _username;
        if (Throttle.IsBlocked(throttleKey, out TimeSpan retryAfter))
        {
            _errors.Add($"Too many wrong attempts. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s).");
            return;
        }

        _isBusy = true;
        try
        {
            bool passwordOk = await Accounts.VerifyCredentialsAsync(_username, _disablePassword) is not null;
            bool codeOk = passwordOk && await TwoFactor.VerifyAsync(_disableCode);
            if (!passwordOk || !codeOk)
            {
                Throttle.RecordFailure(throttleKey);
                await Audit.WriteAsync(_username, "auth.two-factor-disable", null, false, passwordOk ? "wrong code" : "wrong password");
                _errors.Add(passwordOk ? "That code is not valid." : "The password is not correct.");
                return;
            }

            await TwoFactor.DisableAsync();
            await Audit.WriteAsync(_username, "auth.two-factor-disabled", null, true);
            Navigation.NavigateTo("/login?notice=two-factor-changed", forceLoad: true);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task CopyCodesAsync()
    {
        if (_recoveryCodes is not null)
        {
            _copied = await JS.InvokeAsync<bool>("roastgateCopy", string.Join('\n', _recoveryCodes));
        }
    }

    private async Task ContinueAfterCodesAsync()
    {
        if (_signInAfterCodes)
        {
            // Turning it on ended every session (the security stamp changed), this one included: sign in again, with a code.
            Navigation.NavigateTo("/login?notice=two-factor-changed", forceLoad: true);
            return;
        }

        // Regenerating codes changes nothing about the sessions.
        _recoveryCodes = null;
        _copied = false;
        await RefreshAsync();
    }

    private static string BuildQrCode(string uri)
    {
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        SvgQRCode svg = new(data);
        return svg.GetGraphic(4, "#1c1e21", "#ffffff", true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    /// <summary>The secret in groups of four, which is how it is easiest to type.</summary>
    private static string Grouped(string secret)
    {
        return string.Join(' ', Enumerable.Range(0, (secret.Length + 3) / 4).Select((int group) => secret.Substring(group * 4, Math.Min(4, secret.Length - group * 4))));
    }
}
