using Microsoft.AspNetCore.Http;

namespace SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;

public record CreateComplianceCategoryRequest(string Name, string Description, string? Code, bool IsActive = true);
public record CreateComplianceItemRequest(
    Guid ClientId,
    Guid CategoryId,
    string Name,
    string Status,
    Guid? OwnerUserId,
    string RiskLevel,
    string? RequiredDocumentCategory,
    DateTime? DueDateUtc,
    DateTime? ExpiryDateUtc);

public record UpdateComplianceItemRequest(
    string Name,
    string Status,
    Guid? OwnerUserId,
    string RiskLevel,
    string? RequiredDocumentCategory,
    Guid? LinkedDocumentId,
    DateTime? DueDateUtc,
    DateTime? ExpiryDateUtc);

public record CreateComplianceReminderRequest(Guid ComplianceItemId, Guid RecipientUserId, string Type, DateTime ScheduledForUtc);
public record UpdateComplianceReminderStatusRequest(string Status);

public class UploadComplianceEvidenceRequest
{
    public IFormFile File { get; set; } = default!;
    public string? Note { get; set; }
}

public record CreateComplianceWorkflowRequest(string RequestType, DateTime? DueDateUtc, string? Comments);
public record ComplianceEvidenceVersionResponse(
    Guid Id,
    Guid ComplianceItemId,
    Guid ClientId,
    int VersionNumber,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedByUserId,
    string? UploadedBy,
    string? Note,
    bool IsCurrentVersion,
    DateTime UploadedAtUtc,
    string DownloadUrl);
public record ComplianceHistoryEntryResponse(
    Guid Id,
    string Action,
    string Actor,
    string ActorRole,
    DateTime Timestamp,
    string Detail,
    string EntityType,
    Guid EntityId,
    string? MetadataJson);
