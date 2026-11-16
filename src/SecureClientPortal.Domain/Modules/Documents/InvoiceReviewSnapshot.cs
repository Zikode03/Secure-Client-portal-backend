using System.ComponentModel.DataAnnotations;
namespace SecureClientPortal.Backend.Models;

public sealed class InvoiceReviewSnapshot
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? DocumentVersionId { get; set; }
    public int VersionNumber { get; set; }
    public int Revision { get; set; }
    public Guid RequestId { get; set; }
    [MaxLength(64)] public string RequestFingerprint { get; set; } = "";
    [MaxLength(40)] public string RuleSetVersion { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public string InputJson { get; set; } = "{}";
    public string FindingsJson { get; set; } = "[]";
    public Guid ActorUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
