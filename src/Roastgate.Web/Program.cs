using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using Roastgate.Core;
using Roastgate.Core.Data;
using Roastgate.Core.Security;
using Roastgate.Core.Setup;
using Roastgate.Web.Authentication;
using Roastgate.Web.Cli;
using Roastgate.Web.Components;
using Roastgate.Web.Middleware;
using Roastgate.Web.Services;

if (args.Length > 0 && args[0] == "reset-password")
{
    return await ResetPasswordCommand.RunAsync();
}

if (args.Length > 0 && args[0] == "disable-2fa")
{
    return await DisableTwoFactorCommand.RunAsync();
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

RoastgateOptions options = RoastgateOptions.FromEnvironment();
builder.Services.AddRoastgateCore(options);
builder.Services.AddSingleton<SignInTicketService>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddSingleton(RenewalSchedule.Default);
builder.Services.AddHostedService<CertificateRenewalService>();

// Session cookies and antiforgery tokens are encrypted with these keys. They live in the data volume so that
// recreating the container (an upgrade, for instance) does not sign everybody out.
builder.Services.AddDataProtection().SetApplicationName("Roastgate");
builder.Services.AddOptions<KeyManagementOptions>().Configure<RoastgateOptions, ILoggerFactory>(
    (KeyManagementOptions keys, RoastgateOptions current, ILoggerFactory loggerFactory) =>
        keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(current.DataDirectory, "keys")), loggerFactory));
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
        cookie.Events.OnValidatePrincipal = async context =>
        {
            // A cookie issued before the last password change carries an old security stamp: it is no longer valid.
            ISessionStamp sessionStamp = context.HttpContext.RequestServices.GetRequiredService<ISessionStamp>();
            if (!await AuthenticationHelper.IsCurrentAsync(context.Principal, sessionStamp, context.HttpContext.RequestAborted))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, StampRevalidatingAuthenticationStateProvider>();

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

bool databaseExisted = File.Exists(options.DatabasePath);
await app.Services.MigrateRoastgateDatabaseAsync();
app.Logger.LogInformation(
    databaseExisted ? "Using the existing database {DatabasePath}." : "No database found, created {DatabasePath}. If this container used to be set up, its data volume is missing.",
    options.DatabasePath);

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

// The address the signed-in browser connects from, as Roastgate sees it (shown next to "add my address" in the whitelist editor).
app.MapGet("/client-address", (HttpContext context) =>
{
    IPAddress? address = context.Connection.RemoteIpAddress;
    string text = address is null ? string.Empty : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    return Results.Text(text, "text/plain");
}).RequireAuthorization();

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
