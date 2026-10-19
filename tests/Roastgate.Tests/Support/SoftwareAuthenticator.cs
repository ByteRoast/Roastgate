using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Roastgate.Tests.Support;

/// <summary>
/// A WebAuthn authenticator in software (ES256, no attestation), so that the passkey code is tested against real signatures:
/// it answers the options the server produced exactly as a browser would pass them on to a security key.
/// </summary>
internal sealed class SoftwareAuthenticator
{
    private const byte UserPresent = 0x01;
    private const byte UserVerifiedFlag = 0x04;
    private const byte AttestedCredentialData = 0x40;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    public uint Counter { get; set; }

    /// <summary>When false the answers say that no PIN or biometric check happened.</summary>
    public bool UserVerified { get; set; } = true;

    public string Register(string optionsJson, string rpId, string origin)
    {
        using JsonDocument options = JsonDocument.Parse(optionsJson);
        
        string clientData = ClientData("webauthn.create", options, origin);

        byte[] authenticatorData = [.. Header(rpId, Counter, attested: true), .. new byte[16], .. BigEndian((ushort)CredentialId.Length), .. CredentialId, .. CoseKey()];
        
        CborWriter attestation = new();
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString(authenticatorData);
        attestation.WriteEndMap();

        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(CredentialId),
            rawId = Base64Url.EncodeToString(CredentialId),
            type = "public-key",
            clientExtensionResults = new { },
            response = new
            {
                attestationObject = Base64Url.EncodeToString(attestation.Encode()),
                clientDataJSON = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(clientData)),
                transports = Array.Empty<string>(),
            },
        });
    }

    public string Assert(string optionsJson, string rpId, string origin, byte[]? userHandle = null)
    {
        using JsonDocument options = JsonDocument.Parse(optionsJson);
        
        string clientData = ClientData("webauthn.get", options, origin);
        
        Counter++;

        byte[] authenticatorData = Header(rpId, Counter, attested: false);
        byte[] signed = [.. authenticatorData, .. SHA256.HashData(Encoding.UTF8.GetBytes(clientData))];
        byte[] signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(CredentialId),
            rawId = Base64Url.EncodeToString(CredentialId),
            type = "public-key",
            clientExtensionResults = new { },
            response = new
            {
                authenticatorData = Base64Url.EncodeToString(authenticatorData),
                clientDataJSON = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(clientData)),
                signature = Base64Url.EncodeToString(signature),
                userHandle = userHandle is null ? null : Base64Url.EncodeToString(userHandle),
            },
        });
    }

    private static string ClientData(string type, JsonDocument options, string origin)
    {
        return JsonSerializer.Serialize(new
        {
            type,
            challenge = options.RootElement.GetProperty("challenge").GetString(),
            origin,
            crossOrigin = false,
        });
    }

    private byte[] Header(string rpId, uint counter, bool attested)
    {
        byte flags = (byte)(UserPresent | (UserVerified ? UserVerifiedFlag : 0) | (attested ? AttestedCredentialData : 0));
        
        return [.. SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), flags, .. BigEndian(counter)];
    }

    private byte[] CoseKey()
    {
        ECParameters parameters = _key.ExportParameters(false);
        CborWriter cose = new();
        cose.WriteStartMap(5);
        cose.WriteInt32(1);
        cose.WriteInt32(2);
        cose.WriteInt32(3);
        cose.WriteInt32(-7);
        cose.WriteInt32(-1);
        cose.WriteInt32(1);
        cose.WriteInt32(-2);
        cose.WriteByteString(parameters.Q.X!);
        cose.WriteInt32(-3);
        cose.WriteByteString(parameters.Q.Y!);
        cose.WriteEndMap();
        
        return cose.Encode();
    }

    private static byte[] BigEndian(uint value)
    {
        byte[] bytes = new byte[4];
        
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        
        return bytes;
    }

    private static byte[] BigEndian(ushort value)
    {
        byte[] bytes = new byte[2];
        
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        
        return bytes;
    }
}
