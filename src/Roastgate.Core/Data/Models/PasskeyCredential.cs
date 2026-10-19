namespace Roastgate.Core.Data.Models;

/// <summary>A passkey (WebAuthn credential) registered by the administrator.</summary>
public sealed class PasskeyCredential
{
    public int Id { get; set; }

    public required byte[] CredentialId { get; set; }

    /// <summary>The COSE-encoded public key. The private key never leaves the authenticator.</summary>
    public required byte[] PublicKey { get; set; }

    /// <summary>The signature counter of the last accepted sign-in (always 0 for authenticators that do not count, such as synced passkeys).</summary>
    public long SignatureCounter { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
