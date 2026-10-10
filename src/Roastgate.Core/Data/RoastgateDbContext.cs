using Microsoft.EntityFrameworkCore;

namespace Roastgate.Core.Data;

/// <summary>A key/value setting (the structured application settings are stored as one JSON document under the key "app").</summary>
public sealed class Setting
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string Actor { get; set; }

    public required string Action { get; set; }

    public string? Target { get; set; }

    public bool Success { get; set; }

    public string? Details { get; set; }
}

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

public sealed class RoastgateDbContext(DbContextOptions<RoastgateDbContext> options) : DbContext(options)
{
    private const int ConfigNameMaximumLength = 100;

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();

    public DbSet<PasskeyCredential> Passkeys => Set<PasskeyCredential>();

    public DbSet<ConfigBackup> ConfigBackups => Set<ConfigBackup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Setting>(entity =>
        {
            entity.ToTable("settings");
            entity.HasKey(setting => setting.Key);
            entity.Property(setting => setting.Key).HasColumnName("key").HasMaxLength(100);
            entity.Property(setting => setting.Value).HasColumnName("value");
            entity.Property(setting => setting.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_log");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(entry => entry.OccurredAt).HasColumnName("occurred_at");
            entity.Property(entry => entry.Actor).HasColumnName("actor").HasMaxLength(100);
            entity.Property(entry => entry.Action).HasColumnName("action").HasMaxLength(100);
            entity.Property(entry => entry.Target).HasColumnName("target").HasMaxLength(500);
            entity.Property(entry => entry.Success).HasColumnName("success");
            entity.Property(entry => entry.Details).HasColumnName("details");
        });

        modelBuilder.Entity<ConfigBackup>(entity =>
        {
            entity.ToTable("config_backups");
            entity.HasKey(backup => backup.Id);
            entity.Property(backup => backup.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(backup => backup.CreatedAt).HasColumnName("created_at");
            entity.Property(backup => backup.Area).HasColumnName("area").HasMaxLength(50);
            entity.Property(backup => backup.Name).HasColumnName("name").HasMaxLength(ConfigNameMaximumLength);
            entity.Property(backup => backup.Reason).HasColumnName("reason").HasMaxLength(50);
            entity.Property(backup => backup.Content).HasColumnName("content");
            entity.HasIndex(backup => new { backup.Area, backup.Name });
        });

        modelBuilder.Entity<AdminAccount>(entity =>
        {
            entity.ToTable("admin_account", table => table.HasCheckConstraint("ck_admin_account_single_row", "id = 1"));
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(account => account.Username).HasColumnName("username").HasMaxLength(32);
            entity.Property(account => account.PasswordHash).HasColumnName("password_hash");
            entity.Property(account => account.TotpSecret).HasColumnName("totp_secret");
            entity.Property(account => account.TotpLastStep).HasColumnName("totp_last_step");
            entity.Property(account => account.RecoveryCodeHashes).HasColumnName("recovery_codes");
            entity.Property(account => account.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(64).HasDefaultValue(string.Empty);
            entity.Property(account => account.CreatedAt).HasColumnName("created_at");
            entity.Property(account => account.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<PasskeyCredential>(entity =>
        {
            entity.ToTable("passkeys");
            entity.HasKey(passkey => passkey.Id);
            entity.Property(passkey => passkey.Id).HasColumnName("id");
            entity.Property(passkey => passkey.CredentialId).HasColumnName("credential_id");
            entity.HasIndex(passkey => passkey.CredentialId).IsUnique();
            entity.Property(passkey => passkey.PublicKey).HasColumnName("public_key");
            entity.Property(passkey => passkey.SignatureCounter).HasColumnName("signature_counter");
            entity.Property(passkey => passkey.Name).HasColumnName("name").HasMaxLength(64);
            entity.Property(passkey => passkey.CreatedAt).HasColumnName("created_at");
            entity.Property(passkey => passkey.LastUsedAt).HasColumnName("last_used_at");
        });
    }
}
