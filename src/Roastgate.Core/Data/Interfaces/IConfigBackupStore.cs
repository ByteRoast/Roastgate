using Roastgate.Core.Data.Models;

namespace Roastgate.Core.Data.Interfaces;

public interface IConfigBackupStore
{
    Task<long> AddAsync(string area, string name, string reason, string content, CancellationToken cancellationToken = default);

    /// <summary>Newest first. Filters are optional.</summary>
    Task<IReadOnlyList<ConfigBackup>> ListAsync(string? area, string? name, int count, CancellationToken cancellationToken = default);

    Task<ConfigBackup?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Removes a backup, used when the change it was taken for was refused and undone (it would only duplicate the current file).</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
