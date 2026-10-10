using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Roastgate.Core.Certificates;
using Roastgate.Core.Data;
using Roastgate.Core.Hosting;
using Roastgate.Core.Nginx;
using Roastgate.Core.Nginx.Generation;
using Roastgate.Core.Nginx.Whitelists;
using Roastgate.Core.Security;
using Roastgate.Core.Settings;
using Roastgate.Core.Setup;

namespace Roastgate.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRoastgateCore(this IServiceCollection services, RoastgateOptions options)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);

        // The path is read from the registered options at resolution time, so tests can swap the options for a temporary directory.
        services.AddDbContextFactory<RoastgateDbContext>((IServiceProvider provider, DbContextOptionsBuilder builder) =>
        {
            RoastgateOptions current = provider.GetRequiredService<RoastgateOptions>();
            Directory.CreateDirectory(current.DataDirectory);
            builder.UseSqlite($"Data Source={current.DatabasePath};Foreign Keys=True");
        });
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IAuditLog, AuditLog>();
        services.AddSingleton<IAdminAccountStore, AdminAccountStore>();
        services.AddSingleton<IPasskeyStore, PasskeyStore>();
        services.AddSingleton<AppSettingsService>();

        services.AddSingleton<PasswordHasher>(_ => new PasswordHasher());
        services.AddSingleton<ISessionStamp, SessionStamp>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<ITwoFactorService, TwoFactorService>();
        services.AddSingleton<TwoFactorChallenges>();
        services.AddSingleton<PasskeyCeremonies>();
        services.AddSingleton<IPasskeyService, PasskeyService>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<ISetupService, SetupService>();

        services.AddSingleton<IHostExec, HostExec>();
        services.AddSingleton<ILocalExec, LocalExec>();
        services.AddSingleton<NginxProbe>();
        services.AddSingleton<INginxController, NginxController>();
        services.AddSingleton<IConfigBackupStore, ConfigBackupStore>();
        services.AddSingleton<IConfigStore, ConfigStore>();
        services.AddSingleton<IManagedConfigReader, ManagedConfigReader>();
        services.AddSingleton<IWhitelistUsageScanner, WhitelistUsageScanner>();
        services.AddSingleton<IWhitelistService, WhitelistService>();
        services.AddSingleton<ISiteWizardService, SiteWizardService>();
        services.AddSingleton<ICertificateCatalog, CertificateCatalog>();
        services.AddSingleton<ICertificateService, CertificateService>();
        services.AddSingleton<ICertificateRenewalJob, CertificateRenewalJob>();
        services.AddSingleton<HostDiagnostics>();

        return services;
    }
}
