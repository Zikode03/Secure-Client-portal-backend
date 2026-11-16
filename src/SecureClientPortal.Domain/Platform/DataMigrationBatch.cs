using System.ComponentModel.DataAnnotations;
namespace SecureClientPortal.Backend.Models;

// Staging is intentionally separate from Clients, Documents and monthly requirements.
public sealed class DataMigrationBatch
{
    public Guid Id { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "draft";
    public string StateJson { get; set; } = "{}";
    public int Version { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
