namespace Roastgate.Core.Security;

public interface IPasskeyService
{
    /// <summary>True when at least one passkey is registered.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PasskeyInfo>> ListAsync(CancellationToken cancellationToken = default);

    Task<PasskeyChallenge> BeginRegistrationAsync(
        PasskeyRelyingParty relyingParty,
        string username,
        CancellationToken cancellationToken = default
    );

    /// <summary>Stores the new passkey if the authenticator's answer is valid. Returns null otherwise.</summary>
    Task<PasskeyInfo?> CompleteRegistrationAsync(
        PasskeyRelyingParty relyingParty,
        string ceremonyId,
        string responseJson,
        string name,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Starts a sign-in. Without a ticket the browser picks any passkey of this site (sign-in without a password);
    /// with the ticket of a half signed-in user (see <see cref="TwoFactorChallenges"/>) it is the second step.
    /// </summary>
    Task<PasskeyChallenge> BeginSignInAsync(
        PasskeyRelyingParty relyingParty,
        string? ticket,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks the authenticator's answer. Returns the administrator's username, or null when it is not valid.
    /// </summary>
    Task<string?> CompleteSignInAsync(
        PasskeyRelyingParty relyingParty,
        string ceremonyId,
        string responseJson,
        string? ticket,
        CancellationToken cancellationToken = default
    );

    /// <summary>Forgets one passkey. Every open session ends.</summary>
    Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Forgets all passkeys. Every open session ends.</summary>
    Task RemoveAllAsync(CancellationToken cancellationToken = default);
}
