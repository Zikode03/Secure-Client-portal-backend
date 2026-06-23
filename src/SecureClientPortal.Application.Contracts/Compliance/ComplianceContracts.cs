namespace SecureClientPortal.Backend.Application.Contracts;

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
