namespace SecureClientPortal.Backend.Auth;

public class AccessEmailOptions
{
    public const string Section = "AccessEmail";

    public bool Enabled { get; set; }
    public string DeliveryMode { get; set; } = "log";
    public string FromEmail { get; set; } = "no-reply@secureportal.local";
    public string FromName { get; set; } = "Secure Client Portal";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
}
