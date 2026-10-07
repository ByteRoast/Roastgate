using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core.Data;
using Roastgate.Core.Hosting;

namespace Roastgate.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRoastgateCore(this IServiceCollection services, RoastgateOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<RoastgateDatabase>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IAuditLog, AuditLog>();
        services.AddSingleton<IHostExec, HostExec>();
        services.AddSingleton<NginxProbe>();
        return services;
    }
}
