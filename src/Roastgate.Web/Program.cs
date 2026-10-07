using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using Roastgate.Core;
using Roastgate.Core.Data;
using Roastgate.Core.Setup;
using Roastgate.Web.Authentication;
using Roastgate.Web.Cli;
using Roastgate.Web.Components;
using Roastgate.Web.Middleware;

if (args.Length > 0 && args[0] == "reset-password")
{
    return await ResetPasswordCommand.RunAsync();
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

RoastgateOptions options = RoastgateOptions.FromEnvironment();
builder.Services.AddRoastgateCore(options);
builder.Services.AddSingleton<SignInTicketService>();
builder.Services.AddMudServices();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookie =>
    {
        cookie.Cookie.Name = "roastgate.auth";
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.SecurePolicy = options.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        cookie.LoginPath = "/login";
        cookie.ReturnUrlParameter = "returnUrl";
        cookie.AccessDeniedPath = "/login";
        cookie.ExpireTimeSpan = TimeSpan.FromHours(12);
        cookie.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

if (options.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
    });
}

WebApplication app = builder.Build();

await app.Services.MigrateRoastgateDatabaseAsync();

ISetupService setup = app.Services.GetRequiredService<ISetupService>();
await setup.InitializeAsync();
if (!setup.IsInitialized)
{
    app.Logger.LogWarning(
        "Roastgate is not set up yet. Open /setup in the browser and enter this setup token: {SetupToken}",
        setup.SetupToken);
}

if (options.TrustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// TLS is terminated by the host nginx (or a tunnel) in front of Roastgate, so there is no HTTPS redirection here.
app.UseMiddleware<LowercaseRouteMiddleware>();
app.UseMiddleware<SetupGuardMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// Trades a single-use ticket (issued by the setup wizard) for a real cookie sign-in.
app.MapGet("/sign-in/{ticket}", async (string ticket, SignInTicketService tickets, IAuditLog audit, HttpContext context) =>
{
    string? username = tickets.Redeem(ticket);
    if (username is null)
    {
        return Results.Redirect("/login");
    }

    await AuthenticationHelper.SignInAsync(context, username);
    await audit.WriteAsync(username, "auth.login", null, true, "after setup");
    return Results.Redirect("/");
}).AllowAnonymous();

app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery, IAuditLog audit) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest();
    }

    string actor = context.User.Identity?.Name ?? "unknown";
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await audit.WriteAsync(actor, "auth.logout", null, true);
    return Results.Redirect("/login");
}).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
return 0;

public partial class Program;
