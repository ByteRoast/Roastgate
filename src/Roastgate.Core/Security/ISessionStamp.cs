namespace Roastgate.Core.Security;

/// <summary>
/// The security stamp of the account: a random value stored in every session cookie and checked on each request. When
/// the password (or the two-factor setup) really changes, the stamp is renewed, and every session opened before that
/// moment stops working. That is what makes "change the password" actually lock out somebody who stole a session.
/// </summary>
public interface ISessionStamp
{
    /// <summary>The current fingerprint, or null when there is no administrator yet.</summary>
    Task<string?> CurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Forget the cached value after the credentials changed in this process.</summary>
    void Invalidate();
}
