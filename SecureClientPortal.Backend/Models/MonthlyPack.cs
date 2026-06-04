namespace SecureClientPortal.Backend.Models;

public class MonthlyPack
{
    public string Id { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
