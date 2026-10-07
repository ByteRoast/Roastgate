using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Roastgate.Core.Security;

/// <summary>RFC 4648 Base32, the alphabet authenticator apps use for the shared secret.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        StringBuilder builder = new((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bits = 0;
        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return builder.ToString();
    }

    /// <summary>Spaces, hyphens, lowercase and "=" padding are tolerated (secrets are often typed in groups). Null when the text is not Base32.</summary>
    public static byte[]? Decode(string text)
    {
        List<byte> result = [];
        int buffer = 0;
        int bits = 0;
        foreach (char raw in text)
        {
            if (raw is ' ' or '-' or '=')
            {
                continue;
            }

            int index = Alphabet.IndexOf(char.ToUpperInvariant(raw), StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                result.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return result.ToArray();
    }
}

/// <summary>Time based one-time passwords (RFC 6238, HMAC-SHA1, 30 second steps, 6 digits): what every authenticator app implements.</summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;

    /// <summary>One step before and one after the current one are accepted, to absorb clock drift between the phone and the server.</summary>
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
        int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Checks a code against the secret. <paramref name="lastUsedStep"/> is the step of the last code that was accepted: a code of that
    /// step or an earlier one is refused, so a code that was just used (or shoulder-surfed) cannot be replayed.
    /// </summary>
    public static bool TryVerify(string secret, string code, DateTimeOffset now, long? lastUsedStep, out long matchedStep)
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
            bool equal = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(key, step)), Encoding.ASCII.GetBytes(candidate));
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
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }
}

/// <summary>
/// One-time codes for when the phone is lost. 80 random bits each (16 characters), so that storing only a fast SHA-256 of them is
/// safe: there is nothing to guess even for somebody who reads the database.
/// </summary>
public static class RecoveryCodes
{
    public const int Count = 10;

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";
    private const int Length = 16;

    public static IReadOnlyList<string> Generate()
    {
        List<string> codes = [];
        while (codes.Count < Count)
        {
            char[] characters = new char[Length];
            for (int index = 0; index < Length; index++)
            {
                characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            string raw = new(characters);
            string code = string.Join('-', Enumerable.Range(0, 4).Select((int group) => raw.Substring(group * 4, 4)));
            if (!codes.Contains(code))
            {
                codes.Add(code);
            }
        }

        return codes;
    }

    /// <summary>The form a code takes once typed (case, spaces and hyphens do not matter), or null when it cannot be a recovery code.</summary>
    public static string? Normalize(string input)
    {
        string text = new(input.Where((char character) => character is not (' ' or '-')).Select(char.ToLowerInvariant).ToArray());
        return text.Length == Length && text.All((char character) => Alphabet.Contains(character, StringComparison.Ordinal)) ? text : null;
    }

    public static string Hash(string normalizedCode)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("roastgate-recovery|" + normalizedCode))).ToLowerInvariant();
    }
}
