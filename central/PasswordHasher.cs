using System.Security.Cryptography;

namespace Hollowcrown.Central;

/// <summary>PBKDF2-SHA256 salted password hashing — passwords are never stored in plaintext (vision Section 4).</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static (byte[] Salt, byte[] Hash) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return (salt, Derive(password, salt));
    }

    public static bool Verify(string password, byte[] salt, byte[] expectedHash)
    {
        var actual = Derive(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
