using Roastgate.Core;
using Roastgate.Core.Data;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Security;

namespace Roastgate.Web.Cli;

/// <summary>
/// <c>roastgate disable-2fa</c>: the way out when the phone, the recovery codes and the passkeys are all lost
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
        IPasskeyService passkeys = provider.GetRequiredService<IPasskeyService>();

        bool totpEnabled = await twoFactor.IsEnabledAsync();
        bool hasPasskeys = await passkeys.AnyAsync();
        
        if (!totpEnabled && !hasPasskeys)
        {
            await output.WriteLineAsync("Two-factor authentication is not enabled.");
        
            return 0;
        }

        // Passkeys are a second step as well: with one left, a password alone would still not sign anybody in.
        if (totpEnabled)
        {
            await twoFactor.DisableAsync();
        }

        if (hasPasskeys)
        {
            await passkeys.RemoveAllAsync();
        }

        await provider.GetRequiredService<IAuditLog>().WriteAsync("cli", "auth.two-factor-disabled", null, true,
            hasPasskeys ? "passkeys removed" : null);
        
        await output.WriteLineAsync(hasPasskeys
            ? "Two-factor authentication is off and the passkeys were removed. Every open session was ended; sign in with your password."
            : "Two-factor authentication is off. Every open session was ended; sign in with your password.");
        
        return 0;
    }
}
