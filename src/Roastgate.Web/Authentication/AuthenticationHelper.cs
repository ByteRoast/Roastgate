using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Roastgate.Web.Authentication;

public static class AuthenticationHelper
{
    public static Task SignInAsync(HttpContext context, string username)
    {
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, username), new Claim(ClaimTypes.NameIdentifier, username)],
            CookieAuthenticationDefaults.AuthenticationScheme);

        // A session cookie (not persistent): closing the browser signs the administrator out.
        AuthenticationProperties properties = new() { IsPersistent = false, AllowRefresh = true };
        return context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
    }
}
