namespace Roastgate.Core.Data.Models;

/// <summary>A copy of a config file taken just before Roastgate changed or deleted it.</summary>
public sealed class ConfigBackup
{
    public long Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The <see cref="Roastgate.Core.Nginx.ConfigArea"/> name.</summary>
    public required string Area { get; set; }

    public required string Name { get; set; }

    /// <summary>Why the backup was taken (for example "save", "delete", "restore").</summary>
    public required string Reason { get; set; }

    public required string Content { get; set; }
}
