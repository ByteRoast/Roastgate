using System.Security.Cryptography;

namespace Roastgate.Core.Security;

/// <summary>
/// PBKDF2-SHA512 password hashing. Stored format: <c>pbkdf2-sha512$iterations$salt$hash</c> (base64 parts).
/// </summary>
public sealed class PasswordHasher
{
    public const int DefaultIterations = 600_000;

    private const string Scheme = "pbkdf2-sha512";
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly int _iterations;

    public PasswordHasher() : this(DefaultIterations)
    {
    }

    public PasswordHasher(int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        _iterations = iterations;
    }

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _iterations,
            HashAlgorithmName.SHA512,
            HashSize
        );

        return $"{Scheme}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public PasswordVerification Verify(string password, string storedHash)
    {
        string[] parts = storedHash.Split('$');

        if (
            parts.Length != 4
            || parts[0] != Scheme
            || !int.TryParse(parts[1], out int storedIterations)
            || storedIterations < 1
        )
        {
            return PasswordVerification.Failed;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return PasswordVerification.Failed;
        }

        if (expected.Length == 0)
        {
            return PasswordVerification.Failed;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            storedIterations,
            HashAlgorithmName.SHA512,
            expected.Length
        );

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerification.Failed;
        }

        return storedIterations < _iterations
            ? PasswordVerification.SuccessRehashNeeded
            : PasswordVerification.Success;
    }
}
