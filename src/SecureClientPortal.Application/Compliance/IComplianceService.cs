using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application.Compliance;

public interface IComplianceService
{
    Task<IReadOnlyList<ComplianceCategory>> GetCategoriesAsync(CancellationToken ct = default);
    Task<ServiceResult<ComplianceCategory>> CreateCategoryAsync(CreateComplianceCategoryRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<object> SeedDefaultCategoriesAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<object>>> GetItemsAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default);
    Task<ServiceResult<object>> CreateItemAsync(CreateComplianceItemRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> UpdateItemAsync(string id, UpdateComplianceItemRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<object>>> GetAlertsAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<ComplianceReminder>>> GetRemindersAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default);
    Task<ServiceResult<ComplianceReminder>> CreateReminderAsync(CreateComplianceReminderRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<ComplianceReminder>> UpdateReminderStatusAsync(string id, UpdateComplianceReminderStatusRequest request, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct = default);
    Task<ServiceResult<object>> GetSummaryReportAsync(System.Security.Claims.ClaimsPrincipal user, string? clientId = null, CancellationToken ct = default);
}
