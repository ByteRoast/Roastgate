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

    /// <summary>
    /// Spaces, hyphens, lowercase and "=" padding are tolerated (secrets are often typed in groups). Null when the
    /// text is not Base32.
    /// </summary>
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
