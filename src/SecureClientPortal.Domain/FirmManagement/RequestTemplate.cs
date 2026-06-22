namespace SecureClientPortal.Backend.Models;

public class RequestTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RequestType { get; set; } = string.Empty;
    public string TitleTemplate { get; set; } = string.Empty;
    public string DescriptionTemplate { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium";
    public int? DefaultDueInDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
