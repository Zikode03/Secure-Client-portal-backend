using System.Security.Cryptography;
using System.Text;

namespace SecureClientPortal.Backend.Auth;

public static class PasswordHasher
{
    public static string Hash(string plainText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
        return Convert.ToHexString(bytes);
    }

    public static bool Verify(string plainText, string existingHash) =>
        string.Equals(Hash(plainText), existingHash, StringComparison.OrdinalIgnoreCase);
}
