namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateComplianceCategoryRequest(string Name, string Description, string? Code, bool IsActive = true);
public record CreateComplianceItemRequest(
    string ClientId,
    string CategoryId,
    string Name,
    string Status,
    string? OwnerUserId,
    string RiskLevel,
    string? RequiredDocumentCategory,
    DateTime? DueDateUtc,
    DateTime? ExpiryDateUtc);
public record UpdateComplianceItemRequest(
    string Name,
    string Status,
    string? OwnerUserId,
    string RiskLevel,
    string? RequiredDocumentCategory,
    string? LinkedDocumentId,
    DateTime? DueDateUtc,
    DateTime? ExpiryDateUtc);
public record CreateComplianceReminderRequest(string ComplianceItemId, string RecipientUserId, string Type, DateTime ScheduledForUtc);
public record UpdateComplianceReminderStatusRequest(string Status);
