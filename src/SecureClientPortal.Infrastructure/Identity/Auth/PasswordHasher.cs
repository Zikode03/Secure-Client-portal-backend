using System.Security.Cryptography;
using System.Text;

namespace SecureClientPortal.Backend.Auth;

public static class PasswordHasher
{
    private const string Algorithm = "PBKDF2-SHA512";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string plainText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainText);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            plainText,
            salt,
            Iterations,
            HashAlgorithmName.SHA512,
            HashSize);
        return $"{Algorithm}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string plainText, string existingHash)
    {
        if (string.IsNullOrEmpty(plainText) || string.IsNullOrWhiteSpace(existingHash))
        {
            return false;
        }

        if (!existingHash.StartsWith($"{Algorithm}$", StringComparison.Ordinal))
        {
            return VerifyLegacySha256(plainText, existingHash);
        }

        try
        {
            var parts = existingHash.Split('$');
            if (parts.Length != 4 ||
                !int.TryParse(parts[1], out var iterations) ||
                iterations < 100_000)
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                plainText,
                salt,
                iterations,
                HashAlgorithmName.SHA512,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool NeedsRehash(string existingHash)
    {
        if (!existingHash.StartsWith($"{Algorithm}$", StringComparison.Ordinal))
        {
            return true;
        }

        var parts = existingHash.Split('$');
        return parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations < Iterations;
    }

    private static bool VerifyLegacySha256(string plainText, string existingHash)
    {
        if (existingHash.Length != 64)
        {
            return false;
        }

        try
        {
            var expected = Convert.FromHexString(existingHash);
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
