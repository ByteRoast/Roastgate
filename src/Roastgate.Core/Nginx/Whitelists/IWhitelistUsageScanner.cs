namespace Roastgate.Core.Nginx.Whitelists;

public interface IWhitelistUsageScanner
{
    /// <summary>Every include, in the managed configs, that pulls in each of the given whitelists.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<WhitelistUsage>>> FindAsync(
        IReadOnlyCollection<string> whitelistNames,
        CancellationToken cancellationToken = default
    );
}
