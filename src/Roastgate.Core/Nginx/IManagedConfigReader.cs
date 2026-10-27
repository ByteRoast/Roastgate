namespace Roastgate.Core.Nginx;

public interface IManagedConfigReader
{
    /// <summary>Every reverse proxy (conf.d) and site (sites-available) that Roastgate manages.</summary>
    Task<IReadOnlyList<ManagedConfig>> ReadAllAsync(CancellationToken cancellationToken = default);
}
