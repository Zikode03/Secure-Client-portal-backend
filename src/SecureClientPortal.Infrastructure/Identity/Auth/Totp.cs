using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
namespace SecureClientPortal.Backend.Auth;

public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public static string NewSecret() => Encode(RandomNumberGenerator.GetBytes(20));
    public static string Encode(byte[] bytes)
    {
        var result = new StringBuilder(); int bits = 0, value = 0;
        foreach (var b in bytes) { value = (value << 8) | b; bits += 8; while (bits >= 5) { bits -= 5; result.Append(Alphabet[(value >> bits) & 31]); } }
        if (bits > 0) result.Append(Alphabet[(value << (5 - bits)) & 31]);
        return result.ToString();
    }
    public static string Code(string secret, long step, int digits = 6)
    {
        var bytes = new List<byte>(); int value = 0, bits = 0;
        foreach (var c in secret) { var n = Alphabet.IndexOf(c); if (n < 0) throw new FormatException("Invalid secret."); value = (value << 5) | n; bits += 5; if (bits >= 8) { bits -= 8; bytes.Add((byte)(value >> bits)); } }
        Span<byte> counter = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter); var offset = hash[^1] & 15;
        var number = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (number % (digits == 8 ? 100000000 : 1000000)).ToString(digits == 8 ? "D8" : "D6");
    }
    public static long? Verify(string secret, string code, long lastStep, DateTimeOffset now)
    {
        if (code.Length != 6 || code.Any(c => c < '0' || c > '9')) return null;
        var step = now.ToUnixTimeSeconds() / 30;
        foreach (var candidate in new[] { step, step - 1, step + 1 })
            if (candidate > lastStep && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, candidate)), Encoding.ASCII.GetBytes(code)))
                return candidate;
        return null;
    }
}
