using System.Security.Cryptography;
using System.Text;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Security;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Setup;

public sealed record SetupRequest(string Token, string Username, string Password, AppSettings Settings);

public sealed record SetupResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static SetupResult Success { get; } = new(true, []);

    public static SetupResult Failure(params string[] errors)
    {
        return new SetupResult(false, errors);
    }
}

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

public sealed class SetupService(
    IAdminAccountStore accounts,
    AppSettingsService appSettings,
    PasswordHasher hasher,
    IAuditLog audit,
    RoastgateOptions options) : ISetupService
{
    private const int MinimumConfiguredTokenLength = 16;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _initialized;
    private volatile string? _token;

    public bool IsInitialized => _initialized;

    public string? SetupToken => _token;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initialized = await accounts.GetAsync(cancellationToken) is not null;
        
        if (_initialized)
        {
            _token = null;
        
            return;
        }

        if (!string.IsNullOrEmpty(options.SetupToken))
        {
            if (options.SetupToken.Length < MinimumConfiguredTokenLength)
            {
                throw new InvalidOperationException(
                    $"{RoastgateOptions.SetupTokenVariable} must be at least {MinimumConfiguredTokenLength} characters long.");
            }

            _token = options.SetupToken;
        }
        else
        {
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        }
    }

    public bool VerifyToken(string candidate)
    {
        string? token = _token;
        
        if (token is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate.Trim()), Encoding.UTF8.GetBytes(token));
    }

    public async Task<SetupResult> CompleteAsync(SetupRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        
        try
        {
            if (_initialized)
            {
                return SetupResult.Failure("Roastgate is already set up.");
            }

            if (!VerifyToken(request.Token))
            {
                return SetupResult.Failure("The setup token is not valid.");
            }

            string username = request.Username.Trim().ToLowerInvariant();
            
            List<string> problems = [];
            problems.AddRange(CredentialPolicy.ValidateUsername(username));
            problems.AddRange(CredentialPolicy.ValidatePassword(request.Password, username));
            problems.AddRange(request.Settings.Validate());
            
            if (problems.Count > 0)
            {
                return new SetupResult(false, problems);
            }

            await appSettings.SaveAsync(request.Settings, cancellationToken);
            
            if (!await accounts.CreateAsync(username, hasher.Hash(request.Password), cancellationToken))
            {
                return SetupResult.Failure("An administrator already exists.");
            }

            _initialized = true;
            _token = null;
            
            await audit.WriteAsync(username, "setup.completed", null, true, null, cancellationToken);
            
            return SetupResult.Success;
        }
        finally
        {
            _gate.Release();
        }
    }
}
