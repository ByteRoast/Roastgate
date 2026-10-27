namespace Roastgate.Core.Nginx;

public interface INginxController
{
    /// <summary>Runs <c>nginx -t</c> on the host.</summary>
    Task<NginxResult> TestAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs <c>nginx -s reload</c> on the host.</summary>
    Task<NginxResult> ReloadAsync(CancellationToken cancellationToken = default);
}
