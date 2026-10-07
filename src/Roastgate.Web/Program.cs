using MudBlazor.Services;
using Roastgate.Core;
using Roastgate.Core.Data;
using Roastgate.Web.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

RoastgateOptions options = RoastgateOptions.FromEnvironment();
builder.Services.AddRoastgateCore(options);
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

WebApplication app = builder.Build();

await app.Services.GetRequiredService<RoastgateDatabase>().MigrateAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// TLS is terminated by the host nginx (or a tunnel) in front of Roastgate, so there is no HTTPS redirection here.
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
