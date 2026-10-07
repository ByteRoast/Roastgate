using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core;
using Roastgate.Core.Data;
using Roastgate.Core.Security;

namespace Roastgate.Tests.Support;

/// <summary>A fully wired Core service provider on a throw-away SQLite file (fast password hashing, fixed setup token).</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    public const string SetupToken = "unit-test-setup-token-0123456789";

    private readonly string _directory;

    private TestDatabase(string directory, ServiceProvider services)
    {
        _directory = directory;
        Services = services;
    }

    public ServiceProvider Services { get; }

    public static async Task<TestDatabase> CreateAsync(
        Action<RoastgateOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "roastgate-tests-" + Guid.NewGuid().ToString("N"));
        RoastgateOptions options = new()
        {
            DataDirectory = directory,
            HostExecMode = HostExecMode.Direct,
            SetupToken = SetupToken,
        };

        configureOptions?.Invoke(options);

        ServiceCollection services = new();
        services.AddRoastgateCore(options);
        services.AddSingleton(new PasswordHasher(1000));
        configureServices?.Invoke(services);

        ServiceProvider provider = services.BuildServiceProvider();
        await provider.MigrateRoastgateDatabaseAsync();
        return new TestDatabase(directory, provider);
    }

    public T Get<T>()
        where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        // SQLite pools connections per file; release them so the temporary directory can be deleted on Windows.
        SqliteConnection.ClearAllPools();
        TemporaryDirectory.Delete(_directory);
    }
}
