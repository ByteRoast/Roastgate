using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;

namespace Roastgate.Core.Security;

/// <summary>
/// Where the browser is: a passkey is bound to a host name (<see cref="Id"/>) and only works on the origin it was made on.
/// </summary>
public sealed record PasskeyRelyingParty(string Id, string Origin)
{
    public static PasskeyRelyingParty FromUri(Uri uri)
    {
        return new PasskeyRelyingParty(uri.Host, uri.GetLeftPart(UriPartial.Authority));
    }
}

public sealed record PasskeyInfo(int Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>What the browser needs to start a ceremony: the options for <c>navigator.credentials</c>, and the id of the server-side state.</summary>
public sealed record PasskeyChallenge(string CeremonyId, string OptionsJson);

public interface IPasskeyService
{
    /// <summary>True when at least one passkey is registered.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PasskeyInfo>> ListAsync(CancellationToken cancellationToken = default);

    Task<PasskeyChallenge> BeginRegistrationAsync(PasskeyRelyingParty relyingParty, string username,
        CancellationToken cancellationToken = default);

    /// <summary>Stores the new passkey if the authenticator's answer is valid. Returns null otherwise.</summary>
    Task<PasskeyInfo?> CompleteRegistrationAsync(
        PasskeyRelyingParty relyingParty, string ceremonyId, string responseJson, string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a sign-in. Without a ticket the browser picks any passkey of this site (sign-in without a password); with the ticket of a
    /// half signed-in user (see <see cref="TwoFactorChallenges"/>) it is the second step.
    /// </summary>
    Task<PasskeyChallenge> BeginSignInAsync(PasskeyRelyingParty relyingParty, string? ticket,
        CancellationToken cancellationToken = default);

    /// <summary>Checks the authenticator's answer. Returns the administrator's username, or null when it is not valid.</summary>
    Task<string?> CompleteSignInAsync(
        PasskeyRelyingParty relyingParty, string ceremonyId, string responseJson, string? ticket,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets one passkey. Every open session ends.</summary>
    Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Forgets all passkeys. Every open session ends.</summary>
    Task RemoveAllAsync(CancellationToken cancellationToken = default);
}

public sealed class PasskeyService(
    IPasskeyStore passkeys,
    IAdminAccountStore accounts,
    PasskeyCeremonies ceremonies,
    ISessionStamp sessionStamp,
    TimeProvider timeProvider) : IPasskeyService
{
    public const int MaximumNameLength = 64;

    // The account is unique and its name is not a secret, but the WebAuthn user handle must not carry personal data: a fixed opaque value.
    private static readonly byte[] UserHandle = SHA256.HashData(Encoding.UTF8.GetBytes("roastgate:administrator"));

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return (await passkeys.ListAsync(cancellationToken)).Count > 0;
    }

    public async Task<IReadOnlyList<PasskeyInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        return (await passkeys.ListAsync(cancellationToken))
            .Select((PasskeyCredential passkey) =>
                new PasskeyInfo(passkey.Id, passkey.Name, passkey.CreatedAt, passkey.LastUsedAt))
            .ToList();
    }

    public async Task<PasskeyChallenge> BeginRegistrationAsync(PasskeyRelyingParty relyingParty, string username,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PasskeyCredential> existing = await passkeys.ListAsync(cancellationToken);
        CredentialCreateOptions options = Create(relyingParty).RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = UserHandle, Name = username, DisplayName = username },
            ExcludeCredentials = existing.Select((PasskeyCredential passkey) =>
                new PublicKeyCredentialDescriptor(passkey.CredentialId)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                // Discoverable, so that the passkey can also sign in without a username, and always checked with a PIN or biometrics.
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

        string json = options.ToJson();

        return new PasskeyChallenge(ceremonies.Issue(PasskeyCeremonyKind.Registration, json, null), json);
    }

    public async Task<PasskeyInfo?> CompleteRegistrationAsync(
        PasskeyRelyingParty relyingParty, string ceremonyId, string responseJson, string name,
        CancellationToken cancellationToken = default)
    {
        PasskeyCeremony? ceremony = ceremonies.Take(ceremonyId, PasskeyCeremonyKind.Registration);
        AuthenticatorAttestationRawResponse? response = Parse<AuthenticatorAttestationRawResponse>(responseJson);
        
        if (ceremony is null || response is null)
        {
            return null;
        }

        try
        {
            RegisteredPublicKeyCredential credential = await Create(relyingParty).MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = response,
                    OriginalOptions = CredentialCreateOptions.FromJson(ceremony.OptionsJson),
                    IsCredentialIdUniqueToUserCallback = async (IsCredentialIdUniqueToUserParams parameters,
                            CancellationToken token) =>
                        await passkeys.FindAsync(parameters.CredentialId, token) is null,
                },
                cancellationToken);

            string cleanName = name.Trim();
            
            PasskeyCredential passkey = new()
            {
                CredentialId = credential.Id,
                PublicKey = credential.PublicKey,
                SignatureCounter = credential.SignCount,
                Name = cleanName.Length == 0 ? "Passkey" : cleanName[..Math.Min(cleanName.Length, MaximumNameLength)],
                CreatedAt = timeProvider.GetUtcNow(),
            };

            if (!await passkeys.AddAsync(passkey, cancellationToken))
            {
                return null;
            }

            PasskeyCredential stored = (await passkeys.FindAsync(credential.Id, cancellationToken))!;
            
            return new PasskeyInfo(stored.Id, stored.Name, stored.CreatedAt, null);
        }
        catch (Fido2VerificationException)
        {
            return null;
        }
    }

    public async Task<PasskeyChallenge> BeginSignInAsync(PasskeyRelyingParty relyingParty, string? ticket,
        CancellationToken cancellationToken = default)
    {
        List<PublicKeyCredentialDescriptor> allowed = [];
        
        if (ticket is not null)
        {
            allowed = (await passkeys.ListAsync(cancellationToken))
                .Select((PasskeyCredential passkey) => new PublicKeyCredentialDescriptor(passkey.CredentialId))
                .ToList();
        }

        AssertionOptions options = Create(relyingParty).GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowed,
            UserVerification = UserVerificationRequirement.Required,
        });

        string json = options.ToJson();
        
        return new PasskeyChallenge(ceremonies.Issue(PasskeyCeremonyKind.SignIn, json, ticket), json);
    }

    public async Task<string?> CompleteSignInAsync(
        PasskeyRelyingParty relyingParty, string ceremonyId, string responseJson, string? ticket,
        CancellationToken cancellationToken = default)
    {
        PasskeyCeremony? ceremony = ceremonies.Take(ceremonyId, PasskeyCeremonyKind.SignIn);

        // A ceremony started for the second step of one sign-in cannot finish another, and a password-less one cannot finish a second step.
        if (ceremony is null || ceremony.Ticket != ticket)
        {
            return null;
        }

        AuthenticatorAssertionRawResponse? response = Parse<AuthenticatorAssertionRawResponse>(responseJson);
        
        if (response is null)
        {
            return null;
        }

        PasskeyCredential? passkey = await passkeys.FindAsync(response.RawId, cancellationToken);
        AdminAccount? account = await accounts.GetAsync(cancellationToken);
        
        if (passkey is null || account is null)
        {
            return null;
        }

        try
        {
            VerifyAssertionResult result = await Create(relyingParty).MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = response,
                    OriginalOptions = AssertionOptions.FromJson(ceremony.OptionsJson),
                    StoredPublicKey = passkey.PublicKey,
                    StoredSignatureCounter = (uint)passkey.SignatureCounter,
                    IsUserHandleOwnerOfCredentialIdCallback = (IsUserHandleOwnerOfCredentialIdParams parameters,
                            CancellationToken _) =>
                        Task.FromResult(parameters.UserHandle is null ||
                                        parameters.UserHandle.AsSpan().SequenceEqual(UserHandle)),
                },
                cancellationToken);

            // The library already refused an answer without PIN or biometrics (the options require them). The counter is moved only if
            // nobody else used the passkey in the meantime.
            if (!await passkeys.TryRecordUseAsync(passkey.Id, passkey.SignatureCounter, result.SignCount,
                    timeProvider.GetUtcNow(), cancellationToken))
            {
                return null;
            }

            return account.Username;
        }
        catch (Fido2VerificationException)
        {
            return null;
        }
    }

    public async Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!await passkeys.RemoveAsync(id, cancellationToken))
        {
            return false;
        }

        await accounts.RotateSecurityStampAsync(cancellationToken);
        
        sessionStamp.Invalidate();
        
        return true;
    }

    public async Task RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        await passkeys.RemoveAllAsync(cancellationToken);
        await accounts.RotateSecurityStampAsync(cancellationToken);
        
        sessionStamp.Invalidate();
    }

    private static Fido2 Create(PasskeyRelyingParty relyingParty)
    {
        return new Fido2(new Fido2Configuration
        {
            ServerDomain = relyingParty.Id,
            ServerName = TwoFactorService.Issuer,
            Origins = new HashSet<string> { relyingParty.Origin },
        });
    }

    private static T? Parse<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public enum PasskeyCeremonyKind
{
    Registration,
    SignIn,
}

public sealed record PasskeyCeremony(
    PasskeyCeremonyKind Kind,
    string OptionsJson,
    string? Ticket,
    DateTimeOffset ExpiresAt);

/// <summary>
/// The server-side half of a passkey ceremony: the options (with the challenge) that were sent to the browser. Single use, five minutes,
/// so a recorded answer cannot be replayed and an old challenge cannot be answered later.
/// </summary>
public sealed class PasskeyCeremonies(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    // Anybody can start a sign-in ceremony: the table is bounded so that doing it in a loop cannot use up the memory.
    private const int MaximumPending = 1000;

    private readonly ConcurrentDictionary<string, PasskeyCeremony> _pending = new();

    public string Issue(PasskeyCeremonyKind kind, string optionsJson, string? ticket)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (KeyValuePair<string, PasskeyCeremony> expired in _pending.Where((
                     KeyValuePair<string, PasskeyCeremony> pair) => pair.Value.ExpiresAt <= now))
        {
            _pending.TryRemove(expired.Key, out _);
        }

        if (_pending.Count >= MaximumPending)
        {
            foreach (string oldest in _pending
                         .OrderBy((KeyValuePair<string, PasskeyCeremony> pair) => pair.Value.ExpiresAt)
                         .Take(MaximumPending / 10).Select((KeyValuePair<string, PasskeyCeremony> pair) => pair.Key))
            {
                _pending.TryRemove(oldest, out _);
            }
        }

        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        
        _pending[id] = new PasskeyCeremony(kind, optionsJson, ticket, now + Lifetime);
        
        return id;
    }

    /// <summary>The ceremony, which is used up by this call. Null when it is unknown, expired, used or of another kind.</summary>
    public PasskeyCeremony? Take(string id, PasskeyCeremonyKind kind)
    {
        if (_pending.TryRemove(id, out PasskeyCeremony? ceremony) && ceremony.Kind == kind &&
            ceremony.ExpiresAt > timeProvider.GetUtcNow())
        {
            return ceremony;
        }

        return null;
    }
}
