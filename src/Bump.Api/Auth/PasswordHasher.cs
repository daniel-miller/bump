using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Bump.Api.Auth;

/// <summary>
/// Argon2id password hasher. Parameters chosen per OWASP 2024 guidance:
/// 64 MiB memory, 3 iterations, parallelism 1, 16-byte salt, 32-byte hash.
/// </summary>
public static class PasswordHasher
{
    public const string Algorithm = "argon2id";

    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MemoryKb = 64 * 1024;
    private const int Iterations = 3;
    private const int Parallelism = 1;

    public static (byte[] Hash, byte[] Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt);
        return (hash, salt);
    }

    public static bool Verify(string password, byte[] hash, byte[] salt)
    {
        var computed = Derive(password, salt);
        return CryptographicOperations.FixedTimeEquals(computed, hash);
    }

    private static byte[] Derive(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = Parallelism,
            Iterations = Iterations,
            MemorySize = MemoryKb
        };
        return argon2.GetBytes(HashBytes);
    }
}
