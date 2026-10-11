using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Security;
using Roastgate.Web.Services;

namespace Roastgate.Web.Components.Settings;

public partial class PasskeySection
{
    [Inject] private IPasskeyService Passkeys { get; set; } = null!;
    [Inject] private AccountService Accounts { get; set; } = null!;
    [Inject] private LoginThrottle Throttle { get; set; } = null!;
    [Inject] private IAuditLog Audit { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private readonly List<string> _errors = [];
    private IReadOnlyList<PasskeyInfo> _passkeys = [];
    private string _username = string.Empty;
    private string _name = string.Empty;
    private string _password = string.Empty;
    private bool _adding;
    private bool _isBusy;
    private int? _removing;

    private sealed record CreateResult(string? Response, string? Error);

    protected override async Task OnInitializedAsync()
    {
        _username = await User.GetNameAsync();
        _passkeys = await Passkeys.ListAsync();
    }

    private void StartAdd()
    {
        _errors.Clear();
        _name = string.Empty;
        _password = string.Empty;
        _adding = true;
    }

    private void CancelAdd()
    {
        _adding = false;
        _password = string.Empty;
        _errors.Clear();
    }

    private void AskToRemove(int id)
    {
        _errors.Clear();
        _password = string.Empty;
        _removing = id;
    }

    private void CancelRemove()
    {
        _removing = null;
        _password = string.Empty;
        _errors.Clear();
    }

    /// <summary>Checks the password, counting a wrong one against a limit. False (with the problem in <c>_errors</c>) when it is refused.</summary>
    private async Task<bool> ConfirmPasswordAsync()
    {
        string throttleKey = "passkey-confirm:" + _username;
        
        if (Throttle.IsBlocked(throttleKey, out TimeSpan retryAfter))
        {
            _errors.Add($"Too many wrong attempts. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s).");

            return false;
        }

        if (await Accounts.VerifyCredentialsAsync(_username, _password) is null)
        {
            Throttle.RecordFailure(throttleKey);

            await Audit.WriteAsync(_username, "auth.passkey-change", null, false, "wrong password");
            
            _errors.Add("The password is not correct.");
            
            return false;
        }

        Throttle.Reset(throttleKey);
        
        return true;
    }

    private async Task AddAsync()
    {
        _errors.Clear();
        _isBusy = true;
        
        try
        {
            if (!await ConfirmPasswordAsync())
            {
                return;
            }

            PasskeyRelyingParty relyingParty = PasskeyRelyingParty.FromUri(new Uri(Navigation.BaseUri));
            PasskeyChallenge challenge = await Passkeys.BeginRegistrationAsync(relyingParty, _username);
            CreateResult created = await JS.InvokeAsync<CreateResult>("roastgatePasskeyCreate", challenge.OptionsJson);
        
            if (created.Response is null)
            {
                _errors.Add(created.Error ?? "The passkey could not be created.");
                
                return;
            }

            PasskeyInfo? passkey = await Passkeys.CompleteRegistrationAsync(relyingParty, challenge.CeremonyId, created.Response, _name);
            
            if (passkey is null)
            {
                await Audit.WriteAsync(_username, "auth.passkey-added", null, false, "refused by the server");
            
                _errors.Add("The passkey was not accepted. Check that you use the same address as when you signed in.");
                
                return;
            }

            await Audit.WriteAsync(_username, "auth.passkey-added", passkey.Name, true);
            
            _passkeys = await Passkeys.ListAsync();
            _adding = false;
            _password = string.Empty;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task RemoveAsync()
    {
        _errors.Clear();
        
        if (_removing is not { } id)
        {
            return;
        }

        _isBusy = true;
        
        try
        {
            if (!await ConfirmPasswordAsync())
            {
                return;
            }

            string? name = _passkeys.FirstOrDefault(passkey => passkey.Id == id)?.Name;
            
            if (await Passkeys.RemoveAsync(id))
            {
                await Audit.WriteAsync(_username, "auth.passkey-removed", name, true);
            }

            // Removing a credential ended every session (the security stamp changed), this one included.
            Navigation.NavigateTo("/login?notice=two-factor-changed", forceLoad: true);
        }
        finally
        {
            _isBusy = false;
        }
    }
}
