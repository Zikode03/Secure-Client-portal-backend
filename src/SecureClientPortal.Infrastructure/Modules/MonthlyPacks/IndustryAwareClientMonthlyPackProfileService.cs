using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Application.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Data;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Infrastructure.Modules.MonthlyPacks;

/// <summary>
/// Keeps template recommendations based on confirmed operating facts and the client's real Industry.
/// EntityType describes legal form (for example, Private Company) and is intentionally excluded.
/// </summary>
public sealed class IndustryAwareClientMonthlyPackProfileService : IClientMonthlyPackProfileService
{
    private readonly ClientMonthlyPackProfileService _inner;
    private readonly PortalDbContext _db;

    public IndustryAwareClientMonthlyPackProfileService(
        ClientMonthlyPackProfileService inner,
        PortalDbContext db)
    {
        _inner = inner;
        _db = db;
    }

    public async Task<ServiceResult<ClientMonthlyPackProfileDto>> GetAsync(
        Guid clientId,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        await ApplyIndustryRecommendationAsync(
            clientId,
            await _inner.GetAsync(clientId, user, ct),
            ct);

    public async Task<ServiceResult<ClientMonthlyPackProfileDto>> UpdateAsync(
        Guid clientId,
        UpdateClientMonthlyPackProfileRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        await ApplyIndustryRecommendationAsync(
            clientId,
            await _inner.UpdateAsync(clientId, request, user, ct),
            ct);

    public Task<ServiceResult<AddClientMonthlyPackItemResponse>> AddItemAsync(
        Guid clientId,
        AddClientMonthlyPackItemRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        _inner.AddItemAsync(clientId, request, user, ct);

    public async Task<ServiceResult<ClientMonthlyPackProfileDto>> ApproveRecurringAsync(
        Guid clientId,
        Guid requestId,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        await ApplyIndustryRecommendationAsync(
            clientId,
            await _inner.ApproveRecurringAsync(clientId, requestId, user, ct),
            ct);

    public async Task<ServiceResult<ClientMonthlyPackProfileDto>> DeclineRecurringAsync(
        Guid clientId,
        Guid requestId,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        await ApplyIndustryRecommendationAsync(
            clientId,
            await _inner.DeclineRecurringAsync(clientId, requestId, user, ct),
            ct);

    public Task<ServiceResult<MonthlyPackReconciliationResultDto>> ReconcileCurrentPackAsync(
        Guid clientId,
        ClaimsPrincipal user,
        CancellationToken ct = default) =>
        _inner.ReconcileCurrentPackAsync(clientId, user, ct);

    public Task ApplyProfileToPackAsync(
        Guid clientId,
        Guid monthlyPackId,
        CancellationToken ct = default) =>
        _inner.ApplyProfileToPackAsync(clientId, monthlyPackId, ct);

    private async Task<ServiceResult<ClientMonthlyPackProfileDto>> ApplyIndustryRecommendationAsync(
        Guid clientId,
        ServiceResult<ClientMonthlyPackProfileDto> result,
        CancellationToken ct)
    {
        if (result.Value is null)
        {
            return result;
        }

        var client = await _db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == clientId, ct);
        if (client is null)
        {
            return result;
        }

        var recommendation = ResolveRecommendation(
            client.Industry,
            result.Value.OperatingProfile,
            result.Value.AvailableTemplates);

        return result with
        {
            Value = result.Value with
            {
                RecommendedTemplateId = recommendation.TemplateId,
                RecommendedTemplateReason = recommendation.Reason
            }
        };
    }

    internal static (Guid? TemplateId, string? Reason) ResolveRecommendation(
        string? industry,
        ClientOperatingProfileDto? profile,
        IReadOnlyList<ClientMonthlyPackTemplateOptionDto> templates)
    {
        string? keyword = null;
        string? reason = null;
        var industryText = industry?.Trim().ToLowerInvariant() ?? string.Empty;

        if (profile?.ManufacturesGoods == true || industryText.Contains("manufactur") || industryText.Contains("production"))
        {
            keyword = "Manufacturing";
            reason = "Recommended because the business manufactures or produces goods.";
        }
        else if (profile?.UsesBookingPlatforms == true || profile?.UsesFoodSuppliers == true ||
                 industryText.Contains("hospitality") || industryText.Contains("hotel") ||
                 industryText.Contains("restaurant") || industryText.Contains("catering"))
        {
            keyword = "Hospitality";
            reason = "Recommended because the recorded industry or operating profile is hospitality or food service.";
        }
        else if (profile?.UsesSubcontractors == true || profile?.UsesPaymentCertificates == true ||
                 profile?.TracksProjectCosts == true || industryText.Contains("construct") ||
                 industryText.Contains("contractor") || industryText.Contains("engineering"))
        {
            keyword = "Construction";
            reason = "Recommended because the business uses construction, project, contractor, or payment-certificate workflows.";
        }
        else if (profile?.OperatesFleet == true || industryText.Contains("transport") ||
                 industryText.Contains("logistic") || industryText.Contains("fleet") ||
                 industryText.Contains("freight") || industryText.Contains("courier"))
        {
            keyword = "Transport";
            reason = "Recommended because the recorded industry or operating profile involves transport, logistics, or a fleet.";
        }
        else if (profile?.UsesPos == true || profile?.HoldsInventory == true ||
                 industryText.Contains("retail") || industryText.Contains("trading") ||
                 industryText.Contains("wholesale") || industryText.Contains("ecommerce"))
        {
            keyword = "Retail";
            reason = "Recommended because the recorded industry or operating profile involves retail, trading, POS, or inventory.";
        }
        else if (industryText.Contains("professional") || industryText.Contains("consult") ||
                 industryText.Contains("account") || industryText.Contains("legal") ||
                 industryText.Contains("technology") || industryText.Contains("software") ||
                 industryText.Contains("service"))
        {
            keyword = "Professional";
            reason = "Recommended because the recorded industry is a professional or service business.";
        }

        // Do not invent a business classification when Industry is blank and no operating fact
        // identifies the business. In particular, EntityType is never used as a substitute.
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return (null, null);
        }

        var template = templates
            .Where(x => x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name)
            .FirstOrDefault();

        return template is null ? (null, null) : (template.Id, reason);
    }
}
