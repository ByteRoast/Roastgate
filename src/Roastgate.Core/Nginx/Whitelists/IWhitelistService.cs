namespace Roastgate.Core.Nginx.Whitelists;

public interface IWhitelistService
{
    Task<IReadOnlyList<WhitelistSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<WhitelistDetails?> ReadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Validates the entries, then saves through the usual backup, test, rollback and reload cycle.</summary>
    Task<ConfigResult> SaveAsync(
        string name,
        WhitelistDocument document,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Refuses while a config includes the whitelist; otherwise deletes it (nginx -t is still the last safety net).
    /// </summary>
    Task<ConfigResult> DeleteAsync(
        string name,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default
    );
}
