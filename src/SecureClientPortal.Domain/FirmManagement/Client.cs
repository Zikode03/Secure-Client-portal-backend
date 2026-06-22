namespace SecureClientPortal.Backend.Models;

public class Client
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public int ComplianceHealth { get; set; }
    public string AssignedAccountantId { get; set; } = string.Empty;
    public string PrimaryContact { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
