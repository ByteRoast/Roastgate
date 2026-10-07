using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core;
using Roastgate.Core.Data;
using Roastgate.Core.Security;

namespace Roastgate.Web.Cli;

/// <summary>
/// <c>roastgate disable-2fa</c>: the way out when the phone and the recovery codes are both lost
/// (<c>docker exec roastgate roastgate disable-2fa</c>). Whoever can run it already controls the container, so no code is asked.
/// </summary>
internal static class DisableTwoFactorCommand
{
    public static Task<int> RunAsync()
    {
        return RunAsync(RoastgateOptions.FromEnvironment(), Console.Error);
    }

    public static async Task<int> RunAsync(RoastgateOptions options, TextWriter output)
    {
        ServiceCollection services = new();
        services.AddRoastgateCore(options);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await provider.MigrateRoastgateDatabaseAsync();

        ITwoFactorService twoFactor = provider.GetRequiredService<ITwoFactorService>();
        if (!await twoFactor.IsEnabledAsync())
        {
            await output.WriteLineAsync("Two-factor authentication is not enabled.");
            return 0;
        }

        await twoFactor.DisableAsync();
        await provider.GetRequiredService<IAuditLog>().WriteAsync("cli", "auth.two-factor-disabled", null, true);
        await output.WriteLineAsync("Two-factor authentication is off. Every open session was ended; sign in with your password.");
        return 0;
    }
}
