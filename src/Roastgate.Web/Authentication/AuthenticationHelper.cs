using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Roastgate.Core.Security;

namespace Roastgate.Web.Authentication;

public static class AuthenticationHelper
{
    /// <summary>The claim that carries the account's security stamp, checked against the current one on every request.</summary>
    public const string StampClaim = "roastgate:stamp";

    public static async Task SignInAsync(HttpContext context, string username)
    {
        ISessionStamp sessionStamp = context.RequestServices.GetRequiredService<ISessionStamp>();

        // The CLI may have renewed the stamp from another process a moment ago: never put a stale cached value in the cookie.
        sessionStamp.Invalidate();
        string stamp = await sessionStamp.CurrentAsync(context.RequestAborted) ?? string.Empty;

        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, username), new Claim(ClaimTypes.NameIdentifier, username), new Claim(StampClaim, stamp)],
            CookieAuthenticationDefaults.AuthenticationScheme);

        // A session cookie (not persistent): closing the browser signs the administrator out.
        AuthenticationProperties properties = new() { IsPersistent = false, AllowRefresh = true };
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
    }

    /// <summary>True when the principal was signed in with the current security stamp (so before no password change since).</summary>
    public static async Task<bool> IsCurrentAsync(ClaimsPrincipal? principal, ISessionStamp sessionStamp, CancellationToken cancellationToken)
    {
        string? current = await sessionStamp.CurrentAsync(cancellationToken);
        string? claimed = principal?.FindFirstValue(StampClaim);
        return current is not null && claimed == current;
    }
}
