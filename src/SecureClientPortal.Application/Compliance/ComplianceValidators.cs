using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;

namespace SecureClientPortal.Backend.Application.Compliance;

public static class ComplianceValidators
{
    private static readonly HashSet<string> AllowedItemStatuses = ["missing", "pending", "valid", "expiring_soon", "expired", "rejected"];
    private static readonly HashSet<string> AllowedRiskLevels = ["low", "medium", "high", "critical"];
    private static readonly HashSet<string> AllowedReminderStatuses = ["pending", "sent", "dismissed"];

    public static void ValidateCategory(CreateComplianceCategoryRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Category name is required.");
        if (string.IsNullOrWhiteSpace(request.Description)) errors.Add("Category description is required.");
        ThrowIfAny(errors);
    }

    public static void ValidateCreateItem(CreateComplianceItemRequest request)
    {
        var status = request.Status.Trim().ToLowerInvariant();
        var riskLevel = request.RiskLevel.Trim().ToLowerInvariant();
        var errors = new List<string>();
        if (request.ClientId == Guid.Empty) errors.Add("Client id is required.");
        if (request.CategoryId == Guid.Empty) errors.Add("Category id is required.");
        if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Compliance item name is required.");
        if (!AllowedItemStatuses.Contains(status)) errors.Add("Invalid compliance status.");
        if (!AllowedRiskLevels.Contains(riskLevel)) errors.Add("Risk level must be low, medium, high, or critical.");
        ThrowIfAny(errors);
    }

    public static void ValidateUpdateItem(UpdateComplianceItemRequest request)
    {
        var status = request.Status.Trim().ToLowerInvariant();
        var riskLevel = request.RiskLevel.Trim().ToLowerInvariant();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Compliance item name is required.");
        if (!AllowedItemStatuses.Contains(status)) errors.Add("Invalid compliance status.");
        if (!AllowedRiskLevels.Contains(riskLevel)) errors.Add("Risk level must be low, medium, high, or critical.");
        ThrowIfAny(errors);
    }

    public static void ValidateCreateReminder(CreateComplianceReminderRequest request)
    {
        var errors = new List<string>();
        if (request.ComplianceItemId == Guid.Empty) errors.Add("Compliance item id is required.");
        if (request.RecipientUserId == Guid.Empty) errors.Add("Recipient user id is required.");
        if (string.IsNullOrWhiteSpace(request.Type)) errors.Add("Reminder type is required.");
        ThrowIfAny(errors);
    }

    public static void ValidateReminderStatus(UpdateComplianceReminderStatusRequest request)
    {
        var normalized = request.Status.Trim().ToLowerInvariant();
        if (!AllowedReminderStatuses.Contains(normalized))
        {
            throw new AppValidationException("Invalid reminder status.");
        }
    }

    private static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors.ToArray());
        }
    }
}
