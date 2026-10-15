using System.Security.Cryptography;

namespace TypoErp.Modules.Identity.Security;

/// <summary>
///     Кроссплатформенная реализация <see cref="IPasswordHasher" /> на базе PBKDF2-HMAC-SHA256.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string AlgorithmPrefix = "PBKDF2";
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32;
    private const int DefaultIterations = 100_000;

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        Span<byte> salt = stackalloc byte[SaltSizeBytes];
        RandomNumberGenerator.Fill(salt);

        Span<byte> subkey = stackalloc byte[KeySizeBytes];
        Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            subkey,
            DefaultIterations,
            HashAlgorithmName.SHA256
        );

        return $"{AlgorithmPrefix}${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(passwordHash))
            return false;

        var parts = passwordHash.Split('$');
        if (
            parts.Length != 4
            || !string.Equals(parts[0], AlgorithmPrefix, StringComparison.Ordinal)
        )
            return false;

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expectedSubkey;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedSubkey = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expectedSubkey.Length == 0)
            return false;

        var actualSubkey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedSubkey.Length
        );

        return CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
    }
}
