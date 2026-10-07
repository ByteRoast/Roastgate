using System.Security.Cryptography;
using System.Text;
using Roastgate.Core.Data;

namespace Roastgate.Core.Security;

public sealed class AccountService(IAdminAccountStore store, PasswordHasher hasher)
{
    // Anything longer is rejected before hashing so that a huge input cannot be used to burn CPU.
    private const int MaximumAcceptedPasswordLength = 256;

    /// <summary>Returns the account when the credentials are correct, otherwise null. Upgrades the stored hash when needed.</summary>
    public async Task<AdminAccount?> VerifyCredentialsAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        AdminAccount? account = await store.GetAsync(cancellationToken);
        if (account is null || password.Length > MaximumAcceptedPasswordLength)
        {
            return null;
        }

        // The hash is always checked, even when the username is wrong, so both failures take the same time.
        PasswordVerification verification = hasher.Verify(password, account.PasswordHash);
        bool usernameMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(username.Trim().ToLowerInvariant()),
            Encoding.UTF8.GetBytes(account.Username));

        if (verification == PasswordVerification.Failed || !usernameMatches)
        {
            return null;
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            await store.UpdatePasswordHashAsync(hasher.Hash(password), cancellationToken);
        }

        return account;
    }

    /// <summary>Replaces the admin password. Returns the policy problems, or an empty list on success.</summary>
    public async Task<IReadOnlyList<string>> ResetPasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        AdminAccount? account = await store.GetAsync(cancellationToken);
        if (account is null)
        {
            return ["No administrator exists yet. Complete the setup first."];
        }

        IReadOnlyList<string> problems = CredentialPolicy.ValidatePassword(newPassword, account.Username);
        if (problems.Count > 0)
        {
            return problems;
        }

        await store.UpdatePasswordHashAsync(hasher.Hash(newPassword), cancellationToken);
        return [];
    }
}
