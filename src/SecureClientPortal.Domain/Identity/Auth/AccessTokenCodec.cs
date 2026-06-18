using System.Security.Cryptography;
using System.Text;

namespace SecureClientPortal.Backend.Auth;

public static class AccessTokenCodec
{
    public static string GenerateToken()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexString(buffer).ToLowerInvariant();
    }

    public static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(hash);
    }
}
