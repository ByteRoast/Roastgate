using Microsoft.AspNetCore.Antiforgery;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Roastgate.Core.Data;
using Roastgate.Web.Authentication;

namespace Roastgate.Web.Controllers;

public sealed class AuthController : ControllerBase
{
    private readonly SignInTicketService _signInTicketService;
    private readonly IAuditLog _auditLog;
    
    public AuthController(SignInTicketService signInTicketService, IAuditLog auditLog)
    {
        _signInTicketService = signInTicketService;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Trades a single-use ticket (issued by the setup wizard) for a real cookie sign-in.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("/sign-in/{ticket}")]
    public async Task<IActionResult> SignIn(string ticket)
    {
        string? username = this._signInTicketService.Redeem(ticket);

        if (string.IsNullOrEmpty(username))
        {
            return Redirect("/login");
        }
        
        await AuthenticationHelper.SignInAsync(HttpContext, username);
        await _auditLog.WriteAsync(username, "auth.login", null, true, "after setup");

        return Redirect("/");
    }

    [Authorize]
    [HttpGet("/client-address")]
    public IActionResult ClientAddress()
    {
        IPAddress? address = HttpContext.Connection.RemoteIpAddress;

        string text = address is null
            ? string.Empty
            : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();

        return Content(text, "text/plain");
    }

    [Authorize]
    [HttpPost("/logout")]
    public async Task<IActionResult> Logout([FromServices] IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest();
        }

        string actor = HttpContext.User.Identity?.Name ?? "unknown";

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await _auditLog.WriteAsync(actor, "auth.logout", null, true);
        
        return Redirect("/login");
    }
}
