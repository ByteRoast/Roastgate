namespace Roastgate.Core.Data.Models;

/// <summary>The single administrator. <see cref="TotpSecret"/> is reserved for the later two-factor feature.</summary>
public sealed class AdminAccount
{
    /// <summary>Always 1: a check constraint guarantees there is at most one administrator.</summary>
    public int Id { get; set; } = 1;

    public required string Username { get; set; }

    public required string PasswordHash { get; set; }

    public string? TotpSecret { get; set; }

    /// <summary>The time step of the last accepted one-time code. A code of that step or an earlier one is refused (no replay).</summary>
    public long? TotpLastStep { get; set; }

    /// <summary>JSON array of SHA-256 hashes of the unused recovery codes.</summary>
    public string? RecoveryCodeHashes { get; set; }

    /// <summary>
    /// Random value stored in every session cookie. It is renewed whenever the credentials really change (new password, two-factor
    /// enabled or disabled), which ends all open sessions. It is not renewed when a hash is merely upgraded.
    /// </summary>
    public string SecurityStamp { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
