using System.Security.Cryptography;
using System.Text;
using Roastgate.Core.Data;

namespace Roastgate.Core.Security;

public sealed class AccountService(IAdminAccountStore store, PasswordHasher hasher, ISessionStamp sessionStamp)
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
            // Same password, stronger hash: the security stamp is kept, so open sessions stay valid.
            await store.UpdatePasswordHashAsync(hasher.Hash(password), rotateStamp: false, cancellationToken);
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

        await store.UpdatePasswordHashAsync(hasher.Hash(newPassword), rotateStamp: true, cancellationToken);
        sessionStamp.Invalidate();
        return [];
    }

    /// <summary>
    /// Changes the password after checking the current one. Every open session stops working (the sessions carry a fingerprint of the
    /// credentials), so the caller must send the user back to the sign-in page. Returns the problems, or an empty list on success.
    /// </summary>
    public async Task<IReadOnlyList<string>> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        AdminAccount? account = await store.GetAsync(cancellationToken);
        if (account is null)
        {
            return ["No administrator exists yet. Complete the setup first."];
        }

        if (await VerifyCredentialsAsync(account.Username, currentPassword, cancellationToken) is null)
        {
            return ["The current password is not correct."];
        }

        if (currentPassword == newPassword)
        {
            return ["The new password must be different from the current one."];
        }

        return await ResetPasswordAsync(newPassword, cancellationToken);
    }
}
