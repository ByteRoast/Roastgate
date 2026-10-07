using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core.Nginx;
using Roastgate.Core.Settings;

namespace Roastgate.Tests.Support;

/// <summary>
/// A throw-away nginx directory tree plus a fake nginx that "refuses" any managed file containing
/// <see cref="BrokenDirective"/>, the way <c>nginx -t</c> refuses an unknown directive.
/// </summary>
internal sealed class NginxTestbed : IAsyncDisposable
{
    public const string BrokenDirective = "invalid_directive";

    private readonly TestDatabase _database;

    private NginxTestbed(TestDatabase database, string root)
    {
        _database = database;
        Root = root;
        Controller = new FakeNginxController(this);
    }

    public string Root { get; }

    public FakeNginxController Controller { get; }

    public IConfigStore Store => _database.Get<IConfigStore>();

    public TestDatabase Database => _database;

    public string ConfD => Path.Combine(Root, "conf.d");

    public string SitesAvailable => Path.Combine(Root, "sites-available");

    public string SitesEnabled => Path.Combine(Root, "sites-enabled");

    public string Whitelists => Path.Combine(Root, "whitelists");

    public string LetsEncrypt => Path.Combine(Root, "letsencrypt");

    public static async Task<NginxTestbed> CreateAsync(
        bool createDirectories = true,
        Action<IServiceCollection>? configureServices = null,
        Func<AppSettings, AppSettings>? configureSettings = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "roastgate-nginx-" + Guid.NewGuid().ToString("N"));
        NginxTestbed? testbed = null;
        TestDatabase database = await TestDatabase.CreateAsync(configureServices: (IServiceCollection services) =>
        {
            services.AddSingleton<INginxController>(_ => testbed!.Controller);
            configureServices?.Invoke(services);
        });

        testbed = new NginxTestbed(database, root);
        if (createDirectories)
        {
            foreach (string directory in new[] { testbed.ConfD, testbed.SitesAvailable, testbed.SitesEnabled, testbed.Whitelists })
            {
                Directory.CreateDirectory(directory);
            }
        }

        AppSettings settings = new()
        {
            NginxRoot = root,
            WhitelistsDirectory = testbed.Whitelists,
            LetsEncryptDirectory = testbed.LetsEncrypt,
            AcmeWebroot = Path.Combine(root, "acme"),
        };
        await database.Get<AppSettingsService>().SaveAsync(configureSettings is null ? settings : configureSettings(settings));
        return testbed;
    }

    /// <summary>Everything nginx would read: enabled proxies and the sites that are linked.</summary>
    private IEnumerable<string> ActiveFiles()
    {
        if (Directory.Exists(ConfD))
        {
            foreach (string path in Directory.EnumerateFiles(ConfD, "*.conf"))
            {
                yield return path;
            }
        }

        if (Directory.Exists(SitesEnabled))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(SitesEnabled))
            {
                if (File.Exists(path))
                {
                    yield return path;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    public sealed class FakeNginxController(NginxTestbed testbed) : INginxController
    {
        public int Tests { get; private set; }

        public int Reloads { get; private set; }

        public bool ReloadFails { get; set; }

        /// <summary>Extra rule evaluated on every test: returns an nginx error message to fail, or null to pass.</summary>
        public Func<string?>? FailureRule { get; set; }

        public Task<NginxResult> TestAsync(CancellationToken cancellationToken = default)
        {
            Tests++;
            string? ruleFailure = FailureRule?.Invoke();
            if (ruleFailure is not null)
            {
                return Task.FromResult(new NginxResult(false, ruleFailure));
            }

            foreach (string path in testbed.ActiveFiles())
            {
                if (File.ReadAllText(path).Contains(BrokenDirective, StringComparison.Ordinal))
                {
                    return Task.FromResult(new NginxResult(false, $"nginx: [emerg] unknown directive \"{BrokenDirective}\" in {Path.GetFileName(path)}:1"));
                }
            }

            return Task.FromResult(new NginxResult(true, "nginx: configuration file test is successful"));
        }

        public Task<NginxResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            Reloads++;
            return Task.FromResult(ReloadFails ? new NginxResult(false, "nginx: [error] invalid PID number") : new NginxResult(true, string.Empty));
        }
    }
}
