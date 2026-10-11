using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;
using Roastgate.Core.Security;
using Roastgate.Web.Authentication;

namespace Roastgate.Web.Components.Pages;

public partial class Login
{
    [Inject] private AccountService Accounts { get; set; } = null!;
    [Inject] private ITwoFactorService TwoFactor { get; set; } = null!;
    [Inject] private IPasskeyService Passkeys { get; set; } = null!;
    [Inject] private TwoFactorChallenges Challenges { get; set; } = null!;
    [Inject] private LoginThrottle Throttle { get; set; } = null!;
    [Inject] private IAuditLog Audit { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private const int MaximumLoggedUsernameLength = 64;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm(FormName = "login")]
    private LoginInput? Input { get; set; }

    [SupplyParameterFromForm(FormName = "login-code")]
    private CodeInput? Code { get; set; }

    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery(Name = "notice")]
    private string? Notice { get; set; }

    private string? NoticeText => Notice switch
    {
        "password-changed" => "Your password was changed. Sign in again with the new one.",
        "two-factor-changed" => "Two-factor authentication was changed. Sign in again.",
        _ => null,
    };

    private string? _error;
    private string? _ticket;

    // What the second step offers. Until a valid ticket proves otherwise, a code (nothing else is revealed to a stranger).
    private bool _totpEnabled = true;
    private bool _hasPasskeys;

    private string SecondStepHelp => (_totpEnabled, _hasPasskeys) switch
    {
        (true, true) => "Enter the 6-digit code from your authenticator app or one of your recovery codes, or use a passkey.",
        (false, true) => "Use one of your passkeys to finish signing in.",
        _ => "Enter the 6-digit code from your authenticator app, or one of your recovery codes.",
    };

    private string ClientKey => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // The code attempts have their own counter: a correct password must not reset it, or somebody who knows the password could try
    // codes without end.
    private string CodeKey => "2fa:" + ClientKey;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new LoginInput();
        Code ??= new CodeInput();

        // Posting the code rebuilds the page from scratch: the second form must exist again (even for a ticket that has expired, which
        // VerifyCodeAsync reports) or Blazor refuses the submission with "no form on the page has that name".
        if (!string.IsNullOrEmpty(Code.Ticket))
        {
            _ticket = Code.Ticket;
            
            await LoadSecondStepAsync(_ticket);
        }

        if (HttpContext.User.Identity?.IsAuthenticated == true)
        {
            Navigation.NavigateTo(LocalUrl.OrDefault(ReturnUrl, "/"));
        }
    }

    private async Task SignInAsync()
    {
        if (Throttle.IsBlocked(ClientKey, out TimeSpan retryAfter))
        {
            _error = BlockedMessage(retryAfter);
            
            return;
        }

        AdminAccount? account = await Accounts.VerifyCredentialsAsync(Input!.Username, Input.Password);
        if (account is null)
        {
            Throttle.RecordFailure(ClientKey);
            
            string attempted = Input!.Username.Length > MaximumLoggedUsernameLength ? Input.Username[..MaximumLoggedUsernameLength] : Input.Username;
            
            await Audit.WriteAsync("anonymous", "auth.login", attempted, false, $"from {ClientKey}");
            
            _error = "Invalid username or password.";
            
            return;
        }

        // A passkey counts as a second factor too: once one is registered, the password alone is no longer enough.
        if (await TwoFactor.IsEnabledAsync() || await Passkeys.AnyAsync())
        {
            // Only a correct password gets to see that a second step exists.
            _ticket = Challenges.Issue(account.Username);
            
            Code!.Ticket = _ticket;
            Code.Code = string.Empty;
            
            await LoadSecondStepAsync(_ticket);
            
            return;
        }

        Throttle.Reset(ClientKey);
        
        await AuthenticationHelper.SignInAsync(HttpContext, account.Username);
        await Audit.WriteAsync(account.Username, "auth.login", null, true, $"from {ClientKey}");
        
        Navigation.NavigateTo(LocalUrl.OrDefault(ReturnUrl, "/"));
    }

    private async Task LoadSecondStepAsync(string ticket)
    {
        if (Challenges.Peek(ticket) is not null)
        {
            _totpEnabled = await TwoFactor.IsEnabledAsync();
            _hasPasskeys = await Passkeys.AnyAsync();
        }
    }

    private async Task VerifyCodeAsync()
    {
        string ticket = Code!.Ticket;
        string? username = Challenges.Peek(ticket);
        
        if (username is null)
        {
            _ticket = null;
            _error = "That sign-in attempt expired. Enter your password again.";
            
            return;
        }

        _ticket = ticket;
        
        if (Throttle.IsBlocked(CodeKey, out TimeSpan retryAfter))
        {
            _error = BlockedMessage(retryAfter);
        
            return;
        }

        if (await TwoFactor.VerifyAsync(Code.Code))
        {
            Challenges.Complete(ticket);
            
            Throttle.Reset(CodeKey);
            Throttle.Reset(ClientKey);
            
            await AuthenticationHelper.SignInAsync(HttpContext, username);
            await Audit.WriteAsync(username, "auth.login", null, true, $"from {ClientKey}, two-factor");
            
            Navigation.NavigateTo(LocalUrl.OrDefault(ReturnUrl, "/"));
            
            return;
        }

        Throttle.RecordFailure(CodeKey);
        
        await Audit.WriteAsync(username, "auth.two-factor", null, false, $"wrong code from {ClientKey}");
        
        if (Challenges.RecordFailure(ticket))
        {
            Code.Code = string.Empty;
        
            _error = "That code is not valid. Try again.";
            
            return;
        }

        _ticket = null;
        _error = "Too many wrong codes. Enter your password again.";
    }

    private static string BlockedMessage(TimeSpan retryAfter)
    {
        return $"Too many failed attempts. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s).";
    }

    public sealed class LoginInput
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public sealed class CodeInput
    {
        public string Ticket { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;
    }
}
