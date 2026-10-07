using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Roastgate.Core;
using Roastgate.Core.Data;
using Roastgate.Core.Security;

namespace Roastgate.Web.Cli;

/// <summary>
/// <c>roastgate reset-password</c>: replaces the administrator password from inside the container
/// (<c>docker exec -it roastgate roastgate reset-password</c>). The new password is read without echo.
/// </summary>
internal static class ResetPasswordCommand
{
    /// <summary>Entry point used by the application: real environment, real console.</summary>
    public static Task<int> RunAsync()
    {
        return RunAsync(RoastgateOptions.FromEnvironment(), ReadSecretFromConsole, Console.Error);
    }

    /// <param name="options">Where the database lives.</param>
    /// <param name="readSecret">Shows the prompt and returns what the user typed (without echoing it).</param>
    /// <param name="output">Where messages go (standard error, so that standard output stays clean).</param>
    public static async Task<int> RunAsync(RoastgateOptions options, Func<string, string> readSecret, TextWriter output)
    {
        ServiceCollection services = new();
        services.AddRoastgateCore(options);
        await using ServiceProvider provider = services.BuildServiceProvider();

        await provider.MigrateRoastgateDatabaseAsync();

        AdminAccount? account = await provider.GetRequiredService<IAdminAccountStore>().GetAsync();
        if (account is null)
        {
            await output.WriteLineAsync("No administrator exists yet. Open /setup in the browser first.");
            return 1;
        }

        await output.WriteLineAsync($"Resetting the password of '{account.Username}'.");
        string password = readSecret("New password: ");
        string confirmation = readSecret("Repeat the password: ");
        if (password != confirmation)
        {
            await output.WriteLineAsync("The passwords do not match.");
            return 1;
        }

        IReadOnlyList<string> problems = await provider.GetRequiredService<AccountService>().ResetPasswordAsync(password);
        if (problems.Count > 0)
        {
            foreach (string problem in problems)
            {
                await output.WriteLineAsync(problem);
            }

            return 1;
        }

        await provider.GetRequiredService<IAuditLog>().WriteAsync("cli", "auth.password-reset", account.Username, true);
        await output.WriteLineAsync("Password updated.");
        return 0;
    }

    private static string ReadSecretFromConsole(string prompt)
    {
        Console.Error.Write(prompt);
        if (Console.IsInputRedirected)
        {
            string line = Console.In.ReadLine() ?? string.Empty;
            Console.Error.WriteLine();
            return line;
        }

        StringBuilder builder = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return builder.ToString();
    }
}
