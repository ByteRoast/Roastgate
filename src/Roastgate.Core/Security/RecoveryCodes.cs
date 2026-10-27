using System.Security.Cryptography;
using System.Text;

namespace Roastgate.Core.Security;

/// <summary>
/// One-time codes for when the phone is lost. 80 random bits each (16 characters), so that storing only a fast SHA-256
/// of them is safe: there is nothing to guess even for somebody who reads the database.
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

    /// <summary>
    /// The form a code takes once typed (case, spaces and hyphens do not matter), or null when it cannot be a recovery
    /// code.
    /// </summary>
    public static string? Normalize(string input)
    {
        string text = new(
            input.Where((char character) => character is not (' ' or '-')).Select(char.ToLowerInvariant).ToArray()
        );

        return text.Length == Length &&
               text.All((char character) => Alphabet.Contains(character, StringComparison.Ordinal))
            ? text
            : null;
    }

    public static string Hash(string normalizedCode)
    {
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("roastgate-recovery|" + normalizedCode))
            )
            .ToLowerInvariant();
    }
}
