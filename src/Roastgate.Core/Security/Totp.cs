using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Roastgate.Core.Security;

/// <summary>
/// Time based one-time passwords (RFC 6238, HMAC-SHA1, 30 second steps, 6 digits): what every authenticator app
/// implements.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;

    /// <summary>
    /// One step before and one after the current one are accepted, to absorb clock drift between the phone and the
    /// server.
    /// </summary>
    public const int AcceptedDrift = 1;

    private const int SecretBytes = 20;

    public static string GenerateSecret()
    {
        return Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));
    }

    public static long StepOf(DateTimeOffset moment)
    {
        return moment.ToUnixTimeSeconds() / StepSeconds;
    }

    public static string Compute(ReadOnlySpan<byte> key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(key, counter, hash);

        int offset = hash[^1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) |
                     hash[offset + 3];

        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Checks a code against the secret. <paramref name="lastUsedStep"/> is the step of the last code that was
    /// accepted: a code of that step or an earlier one is refused, so a code that was just used (or shoulder-surfed)
    /// cannot be replayed.
    /// </summary>
    public static bool TryVerify(
        string secret,
        string code,
        DateTimeOffset now,
        long? lastUsedStep,
        out long matchedStep
    )
    {
        matchedStep = 0;

        byte[]? key = Base32.Decode(secret);

        string candidate = code.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

        if (key is null || key.Length == 0 || candidate.Length != Digits || !candidate.All(char.IsAsciiDigit))
        {
            return false;
        }

        long current = StepOf(now);

        bool matched = false;

        for (long step = current - AcceptedDrift; step <= current + AcceptedDrift; step++)
        {
            // Every step is compared, whatever happens, so the time taken does not tell which one was close.
            bool equal = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(key, step)),
                Encoding.ASCII.GetBytes(candidate));

            if (equal && !matched && (lastUsedStep is null || step > lastUsedStep))
            {
                matched = true;
                matchedStep = step;
            }
        }

        return matched;
    }

    /// <summary>The <c>otpauth://</c> link that authenticator apps read from a QR code.</summary>
    public static string ProvisioningUri(string issuer, string account, string secret)
    {
        string label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account);

        return
            $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }
}
