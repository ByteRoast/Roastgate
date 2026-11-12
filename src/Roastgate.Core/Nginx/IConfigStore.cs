namespace Roastgate.Core.Nginx;

public interface IConfigStore
{
    Task<ConfigListing> ListAsync(ConfigArea area, CancellationToken cancellationToken = default);

    Task<ConfigFile?> ReadAsync(ConfigArea area, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a file: backup, atomic write, <c>nginx -t</c>, rollback if invalid, reload if asked.
    /// </summary>
    Task<ConfigResult> SaveAsync(
        ConfigArea area,
        string name,
        string content,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default
    );

    Task<ConfigResult> DeleteAsync(
        ConfigArea area,
        string name,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Puts the content of a backup back, through the same cycle as a save. What is replaced is backed up in turn.
    /// </summary>
    Task<ConfigResult> RestoreAsync(
        long backupId,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default
    );

    Task<ConfigResult> SetEnabledAsync(
        ConfigArea area,
        string name,
        bool enabled,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default
    );

    Task<ConfigResult> TestNginxAsync(string actor, CancellationToken cancellationToken = default);

    Task<ConfigResult> ReloadNginxAsync(string actor, CancellationToken cancellationToken = default);
}
