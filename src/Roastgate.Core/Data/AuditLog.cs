using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Roastgate.Core.Data;

public sealed record AuditEntry(
    long Id,
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string? Target,
    bool Success,
    string? Details);

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

public sealed class AuditLog(RoastgateDatabase database) : IAuditLog
{
    public async Task WriteAsync(
        string actor,
        string action,
        string? target,
        bool success,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audit_log (occurred_at, actor, action, target, success, details)
            VALUES ($occurredAt, $actor, $action, $target, $success, $details);
            """;
        command.Parameters.AddWithValue("$occurredAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$target", (object?)target ?? DBNull.Value);
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$details", (object?)details ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, occurred_at, actor, action, target, success, details
            FROM audit_log
            ORDER BY id DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        List<AuditEntry> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new AuditEntry(
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5) == 1,
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return entries;
    }
}
