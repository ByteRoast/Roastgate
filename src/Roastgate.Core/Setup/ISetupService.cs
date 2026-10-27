namespace Roastgate.Core.Setup;

public interface ISetupService
{
    /// <summary>True once the administrator exists. Cheap to read, safe to call on every request.</summary>
    bool IsInitialized { get; }

    /// <summary>The token required to run the setup, or null when the instance is already initialized.</summary>
    string? SetupToken { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    bool VerifyToken(string candidate);

    Task<SetupResult> CompleteAsync(SetupRequest request, CancellationToken cancellationToken = default);
}
