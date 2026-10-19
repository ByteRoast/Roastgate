namespace Roastgate.Core.Data.Models;

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
