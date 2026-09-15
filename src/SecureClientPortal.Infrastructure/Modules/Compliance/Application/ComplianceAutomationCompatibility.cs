using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Domain.Shared.Modules.Requests;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;

// Compatibility for the earlier SystemSettings-backed engine and its scheduler.
// Existing records are copied once, retaining IDs, item links, periods and source JSON.
// The new tables remain authoritative; legacy source records are never deleted.
public sealed partial class ComplianceAutomationService
{
    private static ComplianceRuleDefinition NormalizeLegacyRule(ComplianceRuleDefinition rule) => rule with
    {
        ApplicabilityField = string.IsNullOrEmpty(rule.ApplicabilityField) ? "" :
            char.ToLowerInvariant(rule.ApplicabilityField[0]) + rule.ApplicabilityField[1..],
        CadenceMonths = rule.Code == "CSD" ? 0 : rule.CadenceMonths,
        DueDayOfMonth = rule.Code == "CSD" ? null : rule.DueDayOfMonth,
        RequiresSubmission = rule.Code != "CSD" && rule.RequiresSubmission,
        RequiresPayment = rule.Code != "CSD" && rule.RequiresPayment
    };
    private static ClientComplianceProfile LegacyProfile(ClientComplianceProfile profile) => profile with
    {
        // Earlier profiles used the first month of the VAT period as the anchor.
        VatAnchorMonth = (profile.VatAnchorMonth + profile.VatCycleMonths - 2) % 12 + 1
    };

    private async Task ImportLegacyRulesAsync(CancellationToken ct)
    {
        if (await db.ComplianceAutomationConfigurations.AnyAsync(x => x.Key == "rules", ct)) return;
        var source = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == "compliance.automation.rules", ct);
        if (source is null) return;
        var rules = Decode<ComplianceRuleSet>(source.ValueJson);
        await SetConfiguration("rules", null, rules with { Rules = rules.Rules.Select(NormalizeLegacyRule).ToArray() }, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task ImportLegacyClientAsync(Guid clientId, CancellationToken ct)
    {
        var profileKey = "profile:" + clientId;
        if (!await db.ComplianceAutomationConfigurations.AnyAsync(x => x.Key == profileKey, ct))
        {
            var legacyKey = "compliance.automation.profile:" + clientId.ToString("N");
            var profile = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == legacyKey, ct);
            if (profile is not null)
                await SetConfiguration(profileKey, clientId, LegacyProfile(Decode<ClientComplianceProfile>(profile.ValueJson)), ct);
        }
        var sources = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith("compliance.automation.obligation:")).ToListAsync(ct);
        foreach (var source in sources)
        {
            using var json = JsonDocument.Parse(source.ValueJson);
            var root = json.RootElement;
            if (root.GetProperty("clientId").GetGuid() != clientId) continue;
            var state = Decode<ComplianceObligationResponse>(source.ValueJson);
            if (await db.ComplianceObligations.AnyAsync(x => x.Id == state.Id, ct)) continue;
            // A duplicate period with a different identity must be reconciled explicitly;
            // silently replacing either record could disconnect an existing filing receipt.
            if (await db.ComplianceObligations.AnyAsync(x => x.ClientId == clientId && x.Code == state.Code && x.PeriodStartUtc == state.PeriodStartUtc, ct))
                throw new AppValidationException("Two compliance records use the same business/type/period. Reconcile their evidence links before importing.");
            var item = await db.ComplianceItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == state.Id && x.ClientId == clientId, ct);
            if (item is null) throw new AppValidationException("A legacy compliance record has no matching evidence register item.");
            if (root.TryGetProperty("notApplicable", out var notApplicable) && notApplicable.ValueKind == JsonValueKind.True)
                state = state with { NotApplicableReason = state.NotApplicableReason ?? "Previously marked not applicable" };
            var categories = root.TryGetProperty("requiredDocumentCategories", out var evidence)
                ? evidence.Deserialize<string[]>(Json) ?? [] : [];
            var rule = new ComplianceRuleDefinition(state.Code, state.Name, state.Authority, state.Code,
                state.Code == "CSD" ? 0 : 1, 0, null, "", state.SubmissionStatus != "not_required",
                state.PaymentRequired, categories, state.CreatedAtUtc, null, state.RuleVersion);
            db.ComplianceObligations.Add(new ComplianceObligation
            {
                Id = state.Id, ClientId = clientId, ComplianceItemId = item.Id, Code = state.Code,
                PeriodStartUtc = state.PeriodStartUtc, RuleJson = Encode(rule), StateJson = Encode(state)
            });
            db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(), null, "system", "compliance.legacy_imported",
                "compliance_obligation", state.Id, clientId, Encode(new { source.Key, complianceItemId = item.Id })));
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }

    private async Task ImportLegacyObligationAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        if (await db.ComplianceObligations.AnyAsync(x => x.Id == id, ct)) return;
        var key = "compliance.automation.obligation:" + id.ToString("N");
        var row = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return;
        var state = Decode<ComplianceObligationResponse>(row.ValueJson);
        if (await Access(state.ClientId, user, ct)) await ImportLegacyClientAsync(state.ClientId, ct);
    }

    public async Task<ComplianceAutomationRunResult> RunSystemAsync(DateTime? utcNow = null, CancellationToken ct = default)
    {
        var system = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "admin")], "compliance-scheduler"));
        var result = await RunCoreAsync(null, system, utcNow?.ToUniversalTime() ?? DateTime.UtcNow, ct);
        if (result.Error is not null || result.Value is null)
            throw new InvalidOperationException(result.Error ?? "Scheduled compliance run failed.");
        return result.Value;
    }

    // Preserve source-level entry points used by the remote scheduler/tests and integrations.
    public Task<ServiceResult<ComplianceAutomationRunResult>> RunAsync(ClaimsPrincipal user, Guid? clientId = null, DateTime? utcNow = null, CancellationToken ct = default) =>
        RunCoreAsync(clientId, user, utcNow?.ToUniversalTime() ?? DateTime.UtcNow, ct);
    public Task<ServiceResult<IReadOnlyList<ComplianceObligationResponse>>> GetObligationsAsync(ClaimsPrincipal user, Guid? clientId = null, CancellationToken ct = default) =>
        GetObligationsAsync(clientId, user, ct);
    public Task<ServiceResult<ClientComplianceProfile>> UpdateProfileAsync(Guid id, UpdateClientComplianceProfileRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        UpdateProfileAsync(id, LegacyProfile(Decode<ClientComplianceProfile>(Encode(request))), user, ct);
    public Task<ServiceResult<ComplianceRuleSet>> UpdateRulesAsync(UpdateComplianceRuleSetRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        UpdateRulesAsync(new UpdateComplianceRulesRequest(request.Version, request.Rules.Select(r => NormalizeLegacyRule(Decode<ComplianceRuleDefinition>(Encode(r)))).ToArray()), user, ct);
    public Task<ServiceResult<ComplianceObligationResponse>> RecordPreparationAsync(Guid id, RecordCompliancePreparationRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        PreparationAsync(id, new(request.Complete, request.Note), user, ct);
    public Task<ServiceResult<ComplianceObligationResponse>> RecordReviewAsync(Guid id, RecordComplianceReviewRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        ReviewAsync(id, new(request.Approved, request.Note), user, ct);
    public Task<ServiceResult<ComplianceObligationResponse>> RecordSubmissionAsync(Guid id, RecordComplianceSubmissionRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        SubmissionAsync(id, new(request.SubmittedAtUtc, request.SubmissionReference, request.AmountPayable, request.AmountRefundable, request.PaymentRequired, request.Note), user, ct);
    public Task<ServiceResult<ComplianceObligationResponse>> RecordPaymentAsync(Guid id, RecordCompliancePaymentRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        PaymentAsync(id, new(request.PaidAtUtc, request.PaymentReference, request.AmountPaid, request.Note), user, ct);
    public Task<ServiceResult<ComplianceObligationResponse>> MarkNotApplicableAsync(Guid id, MarkComplianceNotApplicableRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        NotApplicableAsync(id, new(request.Reason), user, ct);

    private async Task<int> EnsureMissingEvidenceRequestAsync(ComplianceObligationResponse state, DateTime now, CancellationToken ct)
    {
        if (state.MissingEvidenceCategories.Length == 0 || state.NotApplicableReason is not null ||
            state.Readiness == "complete" || state.ResponsibleAccountantId is not Guid accountantId || accountantId == Guid.Empty) return 0;
        if (state.DueDateUtc.HasValue && (state.DueDateUtc.Value.Date - now.Date).TotalDays > 21) return 0;
        var title = $"Compliance evidence: {state.Code} {state.PeriodStartUtc:yyyy-MM} to {state.PeriodEndUtc:yyyy-MM}";
        if (db.Requests.Local.Any(x => x.ClientId == state.ClientId && x.Title == title && x.Status != "resolved") ||
            await db.Requests.AnyAsync(x => x.ClientId == state.ClientId && x.Title == title && x.Status != "resolved", ct)) return 0;
        var due = state.DueDateUtc?.AddDays(-7);
        if (due < now) due = state.DueDateUtc;
        db.Requests.Add(RequestItem.Create(Guid.NewGuid(), state.ClientId, "missing_document", null, title,
            $"Please provide the missing evidence required for {state.Code}: {string.Join(", ", state.MissingEvidenceCategories)}.",
            state.DueDateUtc.HasValue && (state.DueDateUtc.Value.Date - now.Date).TotalDays <= 7 ? RequestPriority.High : RequestPriority.Medium,
            accountantId, RequestStatus.WaitingOnClient, due, now));
        return 1;
    }

    private async Task UpdateRegisterAsync(ComplianceObligation row, ComplianceObligationResponse state, CancellationToken ct)
    {
        var item = db.ComplianceItems.Local.FirstOrDefault(x => x.Id == row.ComplianceItemId)
            ?? await db.ComplianceItems.FirstAsync(x => x.Id == row.ComplianceItemId && x.ClientId == row.ClientId, ct);
        var status = state.WorkflowStatus switch
        {
            "complete" or "not_applicable" => ComplianceItemStatus.Valid,
            "overdue" => ComplianceItemStatus.Expired,
            "waiting_for_client" => ComplianceItemStatus.Missing,
            _ => ComplianceItemStatus.Pending
        };
        item.Update(item.Name, status, state.ResponsibleAccountantId, state.WorkflowStatus == "overdue" ? ComplianceRiskLevel.Critical : ComplianceRiskLevel.Medium,
            item.RequiredDocumentCategory, item.LinkedDocumentId, state.DueDateUtc, item.ExpiryDateUtc);
    }

    private static bool EvidenceMatches(string required, string actual)
    {
        var need = Normalize(required); var category = Normalize(actual);
        if (category == need || category.StartsWith(need + "_client_", StringComparison.OrdinalIgnoreCase)) return true;
        return need switch
        {
            "sales_invoices" or "purchase_invoices" => category is "invoices" or "invoice" || category.StartsWith("invoices_client_"),
            "payroll_document" => category is "payroll" or "payroll_document" || category.StartsWith("payroll_client_"),
            "annual_financial_statements" => category is "afs" or "financial_statements",
            "company_records" => category is "company_registration" or "cipc_certificate",
            "csd_registration_report" => category is "csd" or "supplier_registration",
            _ => false
        };
    }
}
