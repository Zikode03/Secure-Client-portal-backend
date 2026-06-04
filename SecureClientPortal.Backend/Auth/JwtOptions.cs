namespace SecureClientPortal.Backend.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "SecureClientPortal";
    public string Audience { get; set; } = "SecureClientPortal.Client";
    public string SigningKey { get; set; } = "CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_KEY_32_CHARS_MIN";
    public int ExpiresMinutes { get; set; } = 120;
}
