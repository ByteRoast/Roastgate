using Roastgate.Core.Data.Models;

namespace Roastgate.Core.Data.Interfaces;

public interface IAuditLog
{
    Task WriteAsync(
        string actor,
        string action,
        string? target,
        bool success,
        string? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>Most recent entries first.</summary>
    Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int count, CancellationToken cancellationToken = default);
}
