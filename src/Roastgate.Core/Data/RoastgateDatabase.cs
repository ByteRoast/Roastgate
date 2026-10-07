using Microsoft.Data.Sqlite;

namespace Roastgate.Core.Data;

/// <summary>Owns the SQLite connection string and the versioned schema (tracked with <c>PRAGMA user_version</c>).</summary>
public sealed class RoastgateDatabase
{
    // Append only: the index + 1 of each entry is the schema version it brings the database to.
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE settings (
            key        TEXT NOT NULL PRIMARY KEY,
            value      TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE audit_log (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            occurred_at TEXT    NOT NULL,
            actor       TEXT    NOT NULL,
            action      TEXT    NOT NULL,
            target      TEXT    NULL,
            success     INTEGER NOT NULL,
            details     TEXT    NULL
        );

        CREATE INDEX ix_audit_log_occurred_at ON audit_log (occurred_at DESC);
        """,
    ];

    private readonly string _connectionString;

    public RoastgateDatabase(RoastgateOptions options)
    {
        Directory.CreateDirectory(options.DataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>Creates the database if needed and applies every pending migration.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);

        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);

        long currentVersion;
        await using (SqliteCommand versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = "PRAGMA user_version;";
            currentVersion = (long)(await versionCommand.ExecuteScalarAsync(cancellationToken))!;
        }

        for (int index = (int)currentVersion; index < Migrations.Length; index++)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, Migrations[index], cancellationToken, transaction);
            // PRAGMA does not accept parameters; the value is an int we computed ourselves.
            await ExecuteAsync(connection, $"PRAGMA user_version = {index + 1};", cancellationToken, transaction);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
