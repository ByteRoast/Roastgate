using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Roastgate.Core;
using Roastgate.Core.Security;
using Roastgate.Core.Setup;

namespace Roastgate.Tests.Web;

/// <summary>The real application on an in-memory test server, backed by a throw-away SQLite file.</summary>
public sealed class RoastgateFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SetupToken = "integration-test-token-0123456789";
    public const string AdminPassword = "Correct-Horse-9";

    /// <summary>The clock the application runs on: tests move it to get new one-time codes instead of waiting.</summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "roastgate-web-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices((IServiceCollection services) =>
        {
            services.AddSingleton(new RoastgateOptions
            {
                DataDirectory = _dataDirectory,
                HostExecMode = HostExecMode.Direct,
                SetupToken = SetupToken,
            });
            services.AddSingleton(new PasswordHasher(1000));
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    public HttpClient CreateBrowser()
    {
        return CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    }

    /// <summary>Runs the setup through the service (the wizard itself is interactive) and returns the admin username.</summary>
    public async Task<string> CompleteSetupAsync()
    {
        ISetupService setup = Services.GetRequiredService<ISetupService>();
        SetupResult result = await setup.CompleteAsync(new SetupRequest(SetupToken, "admin", AdminPassword, new Roastgate.Core.Settings.AppSettings()));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return "admin";
    }

    Task IAsyncLifetime.InitializeAsync()
    {
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Support.TemporaryDirectory.Delete(_dataDirectory);
    }
}
