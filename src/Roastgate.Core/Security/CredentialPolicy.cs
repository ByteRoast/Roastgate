using System.Text.RegularExpressions;

namespace Roastgate.Core.Security;

/// <summary>Rules for the admin username and password. Each method returns human readable problems (empty when valid).</summary>
public static partial class CredentialPolicy
{
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128;

    // A passphrase of this length is accepted without any character class requirement.
    private const int PassphraseLength = 20;

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{2,31}\z")]
    private static partial Regex UsernamePattern();

    public static IReadOnlyList<string> ValidateUsername(string? username)
    {
        if (string.IsNullOrEmpty(username) || !UsernamePattern().IsMatch(username))
        {
            return ["The username must be 3 to 32 characters: lowercase letters, digits, dots, hyphens or underscores, starting with a letter or digit."];
        }

        return [];
    }

    public static IReadOnlyList<string> ValidatePassword(string? password, string? username)
    {
        List<string> problems = [];

        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            problems.Add($"The password must be at least {MinimumPasswordLength} characters long.");
            return problems;
        }

        if (password.Length > MaximumPasswordLength)
        {
            problems.Add($"The password must be at most {MaximumPasswordLength} characters long.");
            return problems;
        }

        if (!string.IsNullOrEmpty(username) && password.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("The password must not contain the username.");
        }

        if (password.Distinct().Count() < 5)
        {
            problems.Add("The password is too repetitive.");
        }

        if (password.Length < PassphraseLength && CountCharacterClasses(password) < 3)
        {
            problems.Add("Use at least 3 of: lowercase letters, uppercase letters, digits, symbols (or a passphrase of 20+ characters).");
        }

        return problems;
    }

    private static int CountCharacterClasses(string password)
    {
        int classes = 0;
        if (password.Any(char.IsLower))
        {
            classes++;
        }

        if (password.Any(char.IsUpper))
        {
            classes++;
        }

        if (password.Any(char.IsDigit))
        {
            classes++;
        }

        if (password.Any((char character) => !char.IsLetterOrDigit(character)))
        {
            classes++;
        }

        return classes;
    }
}
