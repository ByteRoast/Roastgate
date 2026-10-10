using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Roastgate.Core.Data;
using Roastgate.Core.Security;
using Roastgate.Web.Authentication;

namespace Roastgate.Web.Controllers;

/// <summary>
/// Signing in  with a passkey. It cannot be done from a Blazor circuit (no cookie can be set there), so the sign-in page talks to these two
/// endpoints with <c>fetch</c>. Registering a passkey happens in the settings page, which needs no endpoint.
/// </summary>
[RequestSizeLimit(MaximumBodyBytes)]
public sealed class PasskeyController(
    IPasskeyService passkeys,
    TwoFactorChallenges challenges,
    LoginThrottle throttle,
    IAuditLog audit,
    IAntiforgery antiforgery) : ControllerBase
{
    private const int MaximumBodyBytes = 32 * 1024;

    public sealed record OptionsRequest(string? Ticket);

    /// <summary>The answer of the authenticator is passed on as it is: it is parsed and checked by the Fido2 library.</summary>
    public sealed record VerifyRequest(string? CeremonyId, string? Ticket, string? ReturnUrl, JsonElement Response);

    private string ThrottleKey => "passkey:" + (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    [AllowAnonymous]
    [HttpPost("/passkeys/sign-in/options")]
    public async Task<IActionResult> Options([FromBody] OptionsRequest request)
    {
        if (!await IsAntiforgeryValidAsync())
        {
            return BadRequest();
        }

        if (throttle.IsBlocked(ThrottleKey, out TimeSpan retryAfter))
        {
            return TooManyAttempts(retryAfter);
        }

        string? ticket = string.IsNullOrEmpty(request.Ticket) ? null : request.Ticket;
        
        if (ticket is not null && challenges.Peek(ticket) is null)
        {
            return BadRequest(new { error = "That sign-in attempt expired. Enter your password again." });
        }

        if (RelyingParty() is not { } relyingParty)
        {
            return BadRequest();
        }

        PasskeyChallenge challenge = await passkeys.BeginSignInAsync(relyingParty, ticket, HttpContext.RequestAborted);
        
        return Content($"{{\"ceremonyId\":\"{challenge.CeremonyId}\",\"options\":{challenge.OptionsJson}}}",
            "application/json");
    }

    [AllowAnonymous]
    [HttpPost("/passkeys/sign-in/verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest request)
    {
        if (!await IsAntiforgeryValidAsync())
        {
            return BadRequest();
        }

        if (throttle.IsBlocked(ThrottleKey, out TimeSpan retryAfter))
        {
            return TooManyAttempts(retryAfter);
        }

        string? ticket = string.IsNullOrEmpty(request.Ticket) ? null : request.Ticket;
        string? ticketUser = ticket is null ? null : challenges.Peek(ticket);
        
        if (ticket is not null && ticketUser is null)
        {
            return BadRequest(new { error = "That sign-in attempt expired. Enter your password again." });
        }

        if (RelyingParty() is not { } relyingParty || string.IsNullOrEmpty(request.CeremonyId) ||
            request.Response.ValueKind != JsonValueKind.Object)
        {
            return BadRequest();
        }

        string? username = await passkeys.CompleteSignInAsync(
            relyingParty, request.CeremonyId, request.Response.GetRawText(), ticket, HttpContext.RequestAborted);
        
        string client = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        if (username is null)
        {
            throttle.RecordFailure(ThrottleKey);
            
            await audit.WriteAsync(ticketUser ?? "anonymous", "auth.passkey", null, false,
                $"refused passkey from {client}");
            
            if (ticket is not null && !challenges.RecordFailure(ticket))
            {
                return Unauthorized(new
                    { error = "Too many failed attempts. Enter your password again.", restart = true });
            }

            return Unauthorized(new { error = "That passkey was not accepted." });
        }

        if (ticket is not null)
        {
            challenges.Complete(ticket);
        }

        throttle.Reset(ThrottleKey);
        
        await AuthenticationHelper.SignInAsync(HttpContext, username);
        await audit.WriteAsync(username, "auth.login", null, true,
            ticket is null ? $"from {client}, passkey" : $"from {client}, password and passkey");
        
        return Ok(new { redirect = LocalUrl.OrDefault(request.ReturnUrl, "/") });
    }

    private async Task<bool> IsAntiforgeryValidAsync()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private PasskeyRelyingParty? RelyingParty()
    {
        return Uri.TryCreate($"{Request.Scheme}://{Request.Host}", UriKind.Absolute, out Uri? origin)
            ? PasskeyRelyingParty.FromUri(origin)
            : null;
    }

    private ObjectResult TooManyAttempts(TimeSpan retryAfter)
    {
        return StatusCode(StatusCodes.Status429TooManyRequests,
            new
            {
                error = $"Too many failed attempts. Try again in {Math.Ceiling(retryAfter.TotalMinutes)} minute(s)."
            });
    }
}
