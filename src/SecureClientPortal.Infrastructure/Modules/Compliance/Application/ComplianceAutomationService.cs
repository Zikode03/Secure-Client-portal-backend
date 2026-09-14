using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Compliance;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Shared.Modules.Requests;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;

/// <summary>
/// Phase 4 compliance automation engine.
///
/// ComplianceItem remains the durable register entry that existing reports, reminders and evidence
/// workflows already understand. Period/rule/submission/payment metadata is stored in SystemSetting
/// JSON so this phase does not introduce a second competing compliance table or require a risky data
/// migration. Every obligation still receives a stable ComplianceItem id and therefore participates
/// in the existing reminder, evidence and audit infrastructure.
///
/// External filing is intentionally human-controlled: automation prepares and tracks the obligation,
/// but an accountant must explicitly record the submission reference after filing with SARS/CIPC/UIF.
/// </summary>
public sealed class ComplianceAutomationService : IComplianceAutomationService
{
    private const string RulesKey = "compliance.automation.rules";
    private const string ProfilePrefix = "compliance.automation.profile:";
    private const string ObligationPrefix = "compliance.automation.obligation:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PortalDbContext _db;

    public ComplianceAutomationService(PortalDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceResult<ComplianceRuleSetDto>> GetRulesAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!user.IsAdmin() && !user.IsAccountant() && !user.IsClient())
        {
            return ServiceResult<ComplianceRuleSetDto>.ForbiddenResult();
        }

        return ServiceResult<ComplianceRuleSetDto>.Success(await LoadRulesAsync(ct));
    }

    public async Task<ServiceResult<ComplianceRuleSetDto>> UpdateRulesAsync(UpdateComplianceRuleSetRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!user.IsAdmin())
        {
            return ServiceResult<ComplianceRuleSetDto>.ForbiddenResult();
        }

        var validation = ValidateRules(request);
        if (validation is not null)
        {
            return ServiceResult<ComplianceRuleSetDto>.ErrorResult(validation);
        }

        var now = DateTime.UtcNow;
        var normalized = new ComplianceRuleSetDto(
            request.Version.Trim(),
            request.Rules
                .Select(rule => rule with
                {
                    Code = rule.Code.Trim().ToUpperInvariant(),
                    Name = rule.Name.Trim(),
                    Authority = rule.Authority.Trim(),
                    CategoryCode = rule.CategoryCode.Trim().ToUpperInvariant(),
                    ApplicabilityField = rule.ApplicabilityField.Trim(),
                    RequiredDocumentCategories = rule.RequiredDocumentCategories
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_'))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Version = string.IsNullOrWhiteSpace(rule.Version) ? request.Version.Trim() : rule.Version.Trim()
                })
                .ToArray(),
            now);

        await SaveSettingAsync(RulesKey, normalized, ct);
        await _db.WriteAuditLogAsync(
            user,
            "compliance.automation.rules_updated",
            "compliance_rule_set",
            Guid.Empty,
            null,
            JsonSerializer.Serialize(new { normalized.Version, RuleCount = normalized.Rules.Count }, JsonOptions),
            ct);

        return ServiceResult<ComplianceRuleSetDto>.Success(normalized);
    }

    public async Task<ServiceResult<ClientComplianceProfileDto>> GetProfileAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!await CanAccessClientAsync(clientId, user, ct))
        {
            return ServiceResult<ClientComplianceProfileDto>.ForbiddenResult();
        }

        return ServiceResult<ClientComplianceProfileDto>.Success(await LoadProfileAsync(clientId, ct));
    }

    public async Task<ServiceResult<ClientComplianceProfileDto>> UpdateProfileAsync(Guid clientId, UpdateClientComplianceProfileRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!user.IsAdmin() && !user.IsAccountant())
        {
            return ServiceResult<ClientComplianceProfileDto>.ForbiddenResult();
        }
        if (!await CanAccessClientAsync(clientId, user, ct))
        {
            return ServiceResult<ClientComplianceProfileDto>.ForbiddenResult();
        }
        if (request.VatCycleMonths is < 1 or > 12 || request.VatAnchorMonth is < 1 or > 12 || request.FinancialYearEndMonth is < 1 or > 12)
        {
            return ServiceResult<ClientComplianceProfileDto>.ErrorResult("VAT cycle, VAT anchor month and financial year-end month must be between 1 and 12.");
        }

        var profile = new ClientComplianceProfileDto(
            clientId,
            request.VatRegistered,
            request.VatCycleMonths,
            request.VatAnchorMonth,
            request.PayeRegistered,
            request.UifRegistered,
            request.CoidaRegistered,
            request.ProvisionalTaxpayer,
            request.CompanyTaxRegistered,
            request.CipcRegistered,
            request.FinancialYearEndMonth,
            DateTime.UtcNow);

        await SaveSettingAsync(ProfileKey(clientId), profile, ct);
        await _db.WriteAuditLogAsync(
            user,
            "compliance.automation.profile_updated",
            "client",
            clientId,
            clientId,
            JsonSerializer.Serialize(new
            {
                profile.VatRegistered,
                profile.PayeRegistered,
                profile.UifRegistered,
                profile.CoidaRegistered,
                profile.ProvisionalTaxpayer,
                profile.CompanyTaxRegistered,
                profile.CipcRegistered,
                profile.FinancialYearEndMonth
            }, JsonOptions),
            ct);

        return ServiceResult<ClientComplianceProfileDto>.Success(profile);
    }

    public async Task<ServiceResult<IReadOnlyList<ComplianceObligationDto>>> GetObligationsAsync(ClaimsPrincipal user, Guid? clientId = null, CancellationToken ct = default)
    {
        var allowed = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (clientId.HasValue && !allowed.Contains(clientId.Value))
        {
            return ServiceResult<IReadOnlyList<ComplianceObligationDto>>.ForbiddenResult();
        }

        var states = await LoadObligationStatesAsync(ct);
        var filtered = states
            .Where(x => allowed.Contains(x.ClientId) && (!clientId.HasValue || x.ClientId == clientId.Value))
            .OrderBy(x => x.DueDateUtc ?? DateTime.MaxValue)
            .ThenBy(x => x.Code)
            .ToList();

        var clientIds = filtered.Select(x => x.ClientId).Distinct().ToArray();
        var names = await _db.Clients
            .Where(x => clientIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var result = filtered.Select(x => ToDto(x, names.GetValueOrDefault(x.ClientId) ?? "Client")).ToArray();
        return ServiceResult<IReadOnlyList<ComplianceObligationDto>>.Success(result);
    }

    public Task<ServiceResult<ComplianceObligationDto>> RecordPreparationAsync(Guid obligationId, RecordCompliancePreparationRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        MutateObligationAsync(obligationId, user, "compliance.obligation.preparation_recorded", ct, state =>
        {
            if (request.Complete && state.MissingEvidenceCategories.Count > 0)
            {
                return "Preparation cannot be completed while required evidence is still missing.";
            }
            state.PreparationStatus = request.Complete ? "complete" : "in_progress";
            if (!request.Complete) state.ReviewStatus = "not_started";
            state.LastNote = request.Note?.Trim();
            return null;
        });

    public Task<ServiceResult<ComplianceObligationDto>> RecordReviewAsync(Guid obligationId, RecordComplianceReviewRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        MutateObligationAsync(obligationId, user, "compliance.obligation.review_recorded", ct, state =>
        {
            if (request.Approved && state.PreparationStatus != "complete")
            {
                return "The obligation must be prepared before review can be approved.";
            }
            state.ReviewStatus = request.Approved ? "approved" : "changes_required";
            state.LastNote = request.Note?.Trim();
            return null;
        });

    public Task<ServiceResult<ComplianceObligationDto>> RecordSubmissionAsync(Guid obligationId, RecordComplianceSubmissionRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        MutateObligationAsync(obligationId, user, "compliance.obligation.submission_recorded", ct, state =>
        {
            if (state.ReviewStatus != "approved")
            {
                return "Review must be approved before an external submission can be recorded.";
            }
            if (string.IsNullOrWhiteSpace(request.SubmissionReference))
            {
                return "A submission reference is required.";
            }
            if (request.AmountPayable is < 0 || request.AmountRefundable is < 0)
            {
                return "Submission amounts cannot be negative.";
            }

            state.SubmissionStatus = "submitted";
            state.SubmittedAtUtc = request.SubmittedAtUtc.ToUniversalTime();
            state.SubmissionReference = request.SubmissionReference.Trim();
            state.AmountPayable = request.AmountPayable;
            state.AmountRefundable = request.AmountRefundable;
            state.PaymentRequired = request.PaymentRequired;
            state.PaymentStatus = request.PaymentRequired ? "outstanding" : "not_required";
            state.LastNote = request.Note?.Trim();
            return null;
        });

    public Task<ServiceResult<ComplianceObligationDto>> RecordPaymentAsync(Guid obligationId, RecordCompliancePaymentRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        MutateObligationAsync(obligationId, user, "compliance.obligation.payment_recorded", ct, state =>
        {
            if (state.SubmissionStatus != "submitted")
            {
                return "Submission must be recorded before payment can be recorded.";
            }
            if (!state.PaymentRequired)
            {
                return "This obligation does not require a payment.";
            }
            if (request.AmountPaid < 0 || string.IsNullOrWhiteSpace(request.PaymentReference))
            {
                return "A non-negative payment amount and payment reference are required.";
            }

            state.PaymentStatus = "paid";
            state.PaidAtUtc = request.PaidAtUtc.ToUniversalTime();
            state.PaymentReference = request.PaymentReference.Trim();
            state.AmountPaid = request.AmountPaid;
            state.LastNote = request.Note?.Trim();
            return null;
        });

    public Task<ServiceResult<ComplianceObligationDto>> MarkNotApplicableAsync(Guid obligationId, MarkComplianceNotApplicableRequest request, ClaimsPrincipal user, CancellationToken ct = default) =>
        MutateObligationAsync(obligationId, user, "compliance.obligation.not_applicable", ct, state =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason)) return "A reason is required when an obligation is marked not applicable.";
            state.NotApplicable = true;
            state.NotApplicableReason = request.Reason.Trim();
            state.LastNote = request.Reason.Trim();
            return null;
        });

    public async Task<ServiceResult<ComplianceAutomationRunResult>> RunAsync(ClaimsPrincipal user, Guid? clientId = null, DateTime? utcNow = null, CancellationToken ct = default)
    {
        if (!user.IsAdmin() && !user.IsAccountant())
        {
            return ServiceResult<ComplianceAutomationRunResult>.ForbiddenResult();
        }

        var allowed = await user.GetAccessibleClientIdsAsync(_db, ct);
        if (clientId.HasValue && !allowed.Contains(clientId.Value))
        {
            return ServiceResult<ComplianceAutomationRunResult>.ForbiddenResult();
        }

        var target = clientId.HasValue ? new HashSet<Guid> { clientId.Value } : allowed;
        var result = await RunInternalAsync(target, utcNow?.ToUniversalTime() ?? DateTime.UtcNow, ct);
        await _db.WriteAuditLogAsync(
            user,
            "compliance.automation.run",
            "compliance_automation",
            Guid.Empty,
            clientId,
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
        return ServiceResult<ComplianceAutomationRunResult>.Success(result);
    }

    public async Task<ComplianceAutomationRunResult> RunSystemAsync(DateTime? utcNow = null, CancellationToken ct = default)
    {
        var clientIds = (await _db.Clients
            .Where(x => x.Status == "active")
            .Select(x => x.Id)
            .ToListAsync(ct))
            .ToHashSet();
        return await RunInternalAsync(clientIds, utcNow?.ToUniversalTime() ?? DateTime.UtcNow, ct);
    }

    private async Task<ComplianceAutomationRunResult> RunInternalAsync(HashSet<Guid> clientIds, DateTime now, CancellationToken ct)
    {
        var rules = await LoadRulesAsync(ct);
        var clients = await _db.Clients
            .Where(x => clientIds.Contains(x.Id) && x.Status == "active")
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var states = await LoadObligationStatesAsync(ct);
        var warnings = new List<string>();
        var created = 0;
        var refreshed = 0;
        var requestsCreated = 0;

        foreach (var client in clients)
        {
            var profile = await LoadProfileAsync(client.Id, ct);
            foreach (var rule in rules.Rules.Where(x => IsRuleEffective(x, now)))
            {
                var applicability = GetApplicability(profile, rule.ApplicabilityField);
                if (!applicability.HasValue)
                {
                    warnings.Add($"{client.Name}: {rule.Code} applicability is not confirmed.");
                    continue;
                }
                if (!applicability.Value) continue;

                var period = ResolvePeriod(rule, profile, now);
                var existing = states.FirstOrDefault(x =>
                    x.ClientId == client.Id &&
                    string.Equals(x.Code, rule.Code, StringComparison.OrdinalIgnoreCase) &&
                    x.PeriodStartUtc == period.Start &&
                    x.PeriodEndUtc == period.End);

                if (existing is null)
                {
                    var category = await EnsureCategoryAsync(rule.CategoryCode, ct);
                    var dueDate = BuildDueDate(rule, period.End);
                    if (!rule.DueDayOfMonth.HasValue)
                    {
                        warnings.Add($"{rule.Code}: deadline is not configured; set a verified due-day rule before relying on deadline alerts.");
                    }

                    var item = ComplianceItem.Create(
                        Guid.NewGuid(),
                        client.Id,
                        category.Id,
                        BuildObligationName(rule.Code, period.Start, period.End),
                        ComplianceItemStatus.Missing,
                        client.AssignedAccountantId == Guid.Empty ? null : client.AssignedAccountantId,
                        RiskFor(dueDate, now),
                        rule.RequiredDocumentCategories.FirstOrDefault(),
                        dueDate,
                        null,
                        now);
                    _db.ComplianceItems.Add(item);

                    existing = new ObligationState
                    {
                        Id = item.Id,
                        ClientId = client.Id,
                        Code = rule.Code,
                        Name = rule.Name,
                        Authority = rule.Authority,
                        PeriodStartUtc = period.Start,
                        PeriodEndUtc = period.End,
                        DueDateUtc = dueDate,
                        PreparationStatus = "not_started",
                        ReviewStatus = "not_started",
                        SubmissionStatus = rule.RequiresSubmission ? "not_submitted" : "not_required",
                        PaymentRequired = false,
                        PaymentStatus = "not_required",
                        RequiredDocumentCategories = rule.RequiredDocumentCategories.ToList(),
                        ResponsibleAccountantId = client.AssignedAccountantId == Guid.Empty ? null : client.AssignedAccountantId,
                        RuleVersion = rule.Version,
                        CreatedReason = $"Created automatically because the client profile confirms {rule.ApplicabilityField}.",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    states.Add(existing);
                    created++;
                }

                var itemRow = await _db.ComplianceItems.FirstOrDefaultAsync(x => x.Id == existing.Id, ct);
                if (itemRow is null) continue;

                await RefreshEvidenceAsync(existing, ct);
                RecalculateWorkflow(existing, itemRow, now);
                await SaveObligationStateAsync(existing, ct);
                refreshed++;

                if (existing.MissingEvidenceCategories.Count > 0)
                {
                    requestsCreated += await EnsureMissingEvidenceRequestAsync(existing, client.AssignedAccountantId, now, ct);
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return new ComplianceAutomationRunResult(now, clients.Count, created, refreshed, requestsCreated, 0, warnings.Distinct().ToArray());
    }

    private async Task<ServiceResult<ComplianceObligationDto>> MutateObligationAsync(
        Guid obligationId,
        ClaimsPrincipal user,
        string auditAction,
        CancellationToken ct,
        Func<ObligationState, string?> mutation)
    {
        if (!user.IsAdmin() && !user.IsAccountant())
        {
            return ServiceResult<ComplianceObligationDto>.ForbiddenResult();
        }

        var state = await LoadObligationStateAsync(obligationId, ct);
        if (state is null)
        {
            return ServiceResult<ComplianceObligationDto>.NotFoundResult("Compliance obligation was not found.");
        }
        if (!await CanAccessClientAsync(state.ClientId, user, ct))
        {
            return ServiceResult<ComplianceObligationDto>.ForbiddenResult();
        }

        var item = await _db.ComplianceItems.FirstOrDefaultAsync(x => x.Id == obligationId, ct);
        if (item is null)
        {
            return ServiceResult<ComplianceObligationDto>.NotFoundResult("Compliance register item was not found.");
        }

        await RefreshEvidenceAsync(state, ct);
        var error = mutation(state);
        if (error is not null)
        {
            return ServiceResult<ComplianceObligationDto>.ErrorResult(error, statusCode: 409);
        }

        state.UpdatedAtUtc = DateTime.UtcNow;
        RecalculateWorkflow(state, item, DateTime.UtcNow);
        await SaveObligationStateAsync(state, ct);
        await _db.SaveChangesAsync(ct);
        await _db.WriteAuditLogAsync(
            user,
            auditAction,
            "compliance_obligation",
            obligationId,
            state.ClientId,
            JsonSerializer.Serialize(new
            {
                state.Code,
                state.PeriodStartUtc,
                state.PeriodEndUtc,
                state.WorkflowStatus,
                state.PreparationStatus,
                state.ReviewStatus,
                state.SubmissionStatus,
                state.PaymentStatus,
                state.LastNote
            }, JsonOptions),
            ct);

        var clientName = await _db.Clients.Where(x => x.Id == state.ClientId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "Client";
        return ServiceResult<ComplianceObligationDto>.Success(ToDto(state, clientName));
    }

    private async Task RefreshEvidenceAsync(ObligationState state, CancellationToken ct)
    {
        if (state.RequiredDocumentCategories.Count == 0)
        {
            state.MissingEvidenceCategories = [];
            state.EvidenceFound = 0;
            return;
        }

        var packs = await _db.MonthlyPacks
            .Where(x => x.ClientId == state.ClientId)
            .Select(x => new { x.Id, x.Year, x.Month })
            .ToListAsync(ct);
        var startIndex = state.PeriodStartUtc.Year * 12 + state.PeriodStartUtc.Month;
        var endIndex = state.PeriodEndUtc.Year * 12 + state.PeriodEndUtc.Month;
        var packIds = packs
            .Where(x => x.Year * 12 + x.Month >= startIndex && x.Year * 12 + x.Month <= endIndex)
            .Select(x => x.Id)
            .ToArray();

        var categories = await _db.Documents
            .Where(x => x.ClientId == state.ClientId && packIds.Contains(x.MonthlyPackId) && x.Status != "rejected")
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(ct);
        var found = categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.MissingEvidenceCategories = state.RequiredDocumentCategories.Where(x => !found.Contains(x)).ToList();
        state.EvidenceFound = state.RequiredDocumentCategories.Count - state.MissingEvidenceCategories.Count;
    }

    private async Task<int> EnsureMissingEvidenceRequestAsync(ObligationState state, Guid accountantId, DateTime now, CancellationToken ct)
    {
        if (accountantId == Guid.Empty) return 0;
        if (state.DueDateUtc.HasValue && (state.DueDateUtc.Value.Date - now.Date).TotalDays > 21) return 0;

        var periodLabel = $"{state.PeriodStartUtc:yyyy-MM} to {state.PeriodEndUtc:yyyy-MM}";
        var title = $"Compliance evidence: {state.Code} {periodLabel}";
        var alreadyExists = await _db.Requests.AnyAsync(x =>
            x.ClientId == state.ClientId &&
            x.Title == title &&
            x.Status != "resolved",
            ct);
        if (alreadyExists) return 0;

        var requestDue = state.DueDateUtc?.AddDays(-7);
        if (requestDue.HasValue && requestDue.Value < now) requestDue = state.DueDateUtc;
        var request = RequestItem.Create(
            Guid.NewGuid(),
            state.ClientId,
            "missing_document",
            null,
            title,
            $"Please provide the missing evidence required for {state.Code}: {string.Join(", ", state.MissingEvidenceCategories)}.",
            state.DueDateUtc.HasValue && (state.DueDateUtc.Value.Date - now.Date).TotalDays <= 7 ? RequestPriority.High : RequestPriority.Medium,
            accountantId,
            RequestStatus.WaitingOnClient,
            requestDue,
            now);
        _db.Requests.Add(request);
        state.EvidenceRequestId = request.Id;
        return 1;
    }

    private static void RecalculateWorkflow(ObligationState state, ComplianceItem item, DateTime now)
    {
        if (state.NotApplicable)
        {
            state.WorkflowStatus = "not_applicable";
            UpdateRegisterItem(item, ComplianceItemStatus.Valid, ComplianceRiskLevel.Low);
            return;
        }

        var complete = state.SubmissionStatus == "submitted" && (!state.PaymentRequired || state.PaymentStatus == "paid");
        if (complete)
        {
            state.WorkflowStatus = "complete";
            UpdateRegisterItem(item, ComplianceItemStatus.Valid, ComplianceRiskLevel.Low);
            return;
        }

        if (state.DueDateUtc.HasValue && state.DueDateUtc.Value.Date < now.Date)
        {
            state.WorkflowStatus = "overdue";
            UpdateRegisterItem(item, ComplianceItemStatus.Expired, ComplianceRiskLevel.Critical);
            return;
        }

        if (state.SubmissionStatus == "submitted" && state.PaymentRequired && state.PaymentStatus != "paid")
        {
            state.WorkflowStatus = "payment_outstanding";
            UpdateRegisterItem(item, ComplianceItemStatus.Pending, RiskFor(state.DueDateUtc, now));
            return;
        }

        if (state.MissingEvidenceCategories.Count > 0)
        {
            state.WorkflowStatus = "waiting_for_client";
            UpdateRegisterItem(item, ComplianceItemStatus.Missing, RiskFor(state.DueDateUtc, now));
            return;
        }

        if (state.ReviewStatus == "approved") state.WorkflowStatus = "ready_to_file";
        else if (state.PreparationStatus == "complete") state.WorkflowStatus = "ready_for_review";
        else if (state.PreparationStatus == "in_progress") state.WorkflowStatus = "in_preparation";
        else state.WorkflowStatus = "ready_to_prepare";
        UpdateRegisterItem(item, ComplianceItemStatus.Pending, RiskFor(state.DueDateUtc, now));
    }

    private static void UpdateRegisterItem(ComplianceItem item, ComplianceItemStatus status, ComplianceRiskLevel risk)
    {
        item.Update(
            item.Name,
            status,
            item.OwnerUserId,
            risk,
            item.RequiredDocumentCategory,
            item.LinkedDocumentId,
            item.DueDateUtc,
            item.ExpiryDateUtc);
    }

    private async Task<ComplianceRuleSetDto> LoadRulesAsync(CancellationToken ct)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == RulesKey, ct);
        if (setting is null) return BuildStarterRules();
        try
        {
            return JsonSerializer.Deserialize<ComplianceRuleSetDto>(setting.ValueJson, JsonOptions) ?? BuildStarterRules();
        }
        catch (JsonException)
        {
            return BuildStarterRules();
        }
    }

    private static ComplianceRuleSetDto BuildStarterRules()
    {
        var effective = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Deadlines are deliberately null in the starter set. They must be verified and configured by
        // the firm before deadline alerts are relied upon; regulatory dates must not be silently baked into code.
        ComplianceRuleDefinitionDto Rule(string code, string name, string authority, string category, int cadence, string field, bool payment, params string[] evidence) =>
            new(code, name, authority, category, cadence, 1, null, field, true, payment, evidence, effective, null, "starter-2026.1");

        return new ComplianceRuleSetDto(
            "starter-2026.1",
            new[]
            {
                Rule("VAT201", "VAT201 return", "SARS", "TAX", 2, "VatRegistered", true, "bank_statement", "sales_invoices", "purchase_invoices"),
                Rule("EMP201", "EMP201 employer declaration", "SARS", "PAYROLL", 1, "PayeRegistered", true, true, "payroll_document"),
                Rule("EMP501", "EMP501 reconciliation", "SARS", "PAYROLL", 6, "PayeRegistered", true, "payroll_document"),
                Rule("IRP6", "IRP6 provisional tax", "SARS", "TAX", 6, "ProvisionalTaxpayer", true, true, "management_accounts"),
                Rule("ITR14", "ITR14 company income tax return", "SARS", "TAX", 12, "CompanyTaxRegistered", false, "annual_financial_statements"),
                Rule("UIF", "UIF declaration/payment", "UIF", "PAYROLL", 1, "UifRegistered", true, "payroll_document"),
                Rule("COIDA", "COIDA return of earnings", "Compensation Fund", "PAYROLL", 12, "CoidaRegistered", true, "payroll_document"),
                Rule("CIPC_AR", "CIPC annual return", "CIPC", "CIPC", 12, "CipcRegistered", true, "company_records")
            },
            effective);
    }

    private async Task<ClientComplianceProfileDto> LoadProfileAsync(Guid clientId, CancellationToken ct)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == ProfileKey(clientId), ct);
        if (setting is not null)
        {
            try
            {
                var saved = JsonSerializer.Deserialize<ClientComplianceProfileDto>(setting.ValueJson, JsonOptions);
                if (saved is not null) return saved;
            }
            catch (JsonException) { }
        }

        return new ClientComplianceProfileDto(clientId, null, 2, 1, null, null, null, null, null, null, 2, DateTime.UtcNow);
    }

    private async Task<List<ObligationState>> LoadObligationStatesAsync(CancellationToken ct)
    {
        var settings = await _db.SystemSettings
            .Where(x => x.Key.StartsWith(ObligationPrefix))
            .ToListAsync(ct);
        var result = new List<ObligationState>();
        foreach (var setting in settings)
        {
            try
            {
                var state = JsonSerializer.Deserialize<ObligationState>(setting.ValueJson, JsonOptions);
                if (state is not null) result.Add(state);
            }
            catch (JsonException) { }
        }
        return result;
    }

    private async Task<ObligationState?> LoadObligationStateAsync(Guid id, CancellationToken ct)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == ObligationKey(id), ct);
        if (setting is null) return null;
        try { return JsonSerializer.Deserialize<ObligationState>(setting.ValueJson, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private Task SaveObligationStateAsync(ObligationState state, CancellationToken ct)
    {
        state.UpdatedAtUtc = DateTime.UtcNow;
        return SaveSettingAsync(ObligationKey(state.Id), state, ct);
    }

    private async Task SaveSettingAsync<T>(string key, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (setting is null) _db.SystemSettings.Add(SystemSetting.Create(key, json));
        else setting.UpdateValue(json);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ComplianceCategory> EnsureCategoryAsync(string categoryCode, CancellationToken ct)
    {
        var code = categoryCode.Trim().ToUpperInvariant();
        var existing = await _db.ComplianceCategories.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (existing is not null) return existing;

        var details = code switch
        {
            "PAYROLL" => ("Payroll Compliance", "Employer declarations, payroll taxes and labour-related compliance."),
            "CIPC" => ("CIPC Compliance", "Company registration and annual-return obligations."),
            _ => ("Tax Compliance", "Tax registrations, returns, payments and supporting evidence.")
        };
        var created = ComplianceCategory.Create(Guid.NewGuid(), details.Item1, details.Item2, code);
        _db.ComplianceCategories.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<bool> CanAccessClientAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct)
    {
        var allowed = await user.GetAccessibleClientIdsAsync(_db, ct);
        return allowed.Contains(clientId);
    }

    private static bool? GetApplicability(ClientComplianceProfileDto profile, string field) => field.Trim() switch
    {
        "VatRegistered" => profile.VatRegistered,
        "PayeRegistered" => profile.PayeRegistered,
        "UifRegistered" => profile.UifRegistered,
        "CoidaRegistered" => profile.CoidaRegistered,
        "ProvisionalTaxpayer" => profile.ProvisionalTaxpayer,
        "CompanyTaxRegistered" => profile.CompanyTaxRegistered,
        "CipcRegistered" => profile.CipcRegistered,
        _ => null
    };

    private static (DateTime Start, DateTime End) ResolvePeriod(ComplianceRuleDefinitionDto rule, ClientComplianceProfileDto profile, DateTime now)
    {
        var cadence = rule.Code.Equals("VAT201", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(profile.VatCycleMonths, 1, 12)
            : Math.Clamp(rule.CadenceMonths, 1, 12);
        if (cadence == 1)
        {
            var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return (start, start.AddMonths(1).AddDays(-1));
        }

        if (cadence == 12 && (rule.Code is "ITR14" or "CIPC_AR" or "COIDA"))
        {
            var fyEndMonth = Math.Clamp(profile.FinancialYearEndMonth, 1, 12);
            var endYear = now.Month <= fyEndMonth ? now.Year : now.Year + 1;
            var end = new DateTime(endYear, fyEndMonth, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddDays(-1);
            return (end.AddMonths(-11).AddDays(1 - end.Day), end);
        }

        var anchorMonth = rule.Code.Equals("VAT201", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(profile.VatAnchorMonth, 1, 12)
            : 1;
        var absolute = now.Year * 12 + now.Month - 1;
        var anchorAbsolute = now.Year * 12 + anchorMonth - 1;
        while (anchorAbsolute > absolute) anchorAbsolute -= 12;
        var periodsSinceAnchor = Math.Max(0, (absolute - anchorAbsolute) / cadence);
        var startAbsolute = anchorAbsolute + periodsSinceAnchor * cadence;
        var start = new DateTime(startAbsolute / 12, startAbsolute % 12 + 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(cadence).AddDays(-1));
    }

    private static DateTime? BuildDueDate(ComplianceRuleDefinitionDto rule, DateTime periodEnd)
    {
        if (!rule.DueDayOfMonth.HasValue) return null;
        var target = new DateTime(periodEnd.Year, periodEnd.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(Math.Clamp(rule.DueOffsetMonths, 0, 24));
        var day = Math.Min(rule.DueDayOfMonth.Value, DateTime.DaysInMonth(target.Year, target.Month));
        return new DateTime(target.Year, target.Month, day, 23, 59, 59, DateTimeKind.Utc);
    }

    private static ComplianceRiskLevel RiskFor(DateTime? dueDate, DateTime now)
    {
        if (!dueDate.HasValue) return ComplianceRiskLevel.Medium;
        var days = (dueDate.Value.Date - now.Date).TotalDays;
        if (days < 0) return ComplianceRiskLevel.Critical;
        if (days <= 7) return ComplianceRiskLevel.High;
        if (days <= 21) return ComplianceRiskLevel.Medium;
        return ComplianceRiskLevel.Low;
    }

    private static bool IsRuleEffective(ComplianceRuleDefinitionDto rule, DateTime now) =>
        rule.EffectiveFromUtc <= now && (!rule.EffectiveToUtc.HasValue || rule.EffectiveToUtc.Value >= now);

    private static string BuildObligationName(string code, DateTime start, DateTime end) =>
        $"{code} {start:MMM yyyy}" + (start.Month == end.Month && start.Year == end.Year ? string.Empty : $" – {end:MMM yyyy}");

    private static string? ValidateRules(UpdateComplianceRuleSetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Version)) return "A rule-set version is required.";
        if (request.Rules.Count == 0) return "At least one compliance rule is required.";
        if (request.Rules.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Authority))) return "Every rule needs a code, name and authority.";
        if (request.Rules.Any(x => x.CadenceMonths is < 1 or > 12)) return "Rule cadence must be between 1 and 12 months.";
        if (request.Rules.Any(x => x.DueOffsetMonths is < 0 or > 24)) return "Due-date offset must be between 0 and 24 months.";
        if (request.Rules.Any(x => x.DueDayOfMonth is < 1 or > 31)) return "Due day must be between 1 and 31 when configured.";
        if (request.Rules.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) return "Compliance rule codes must be unique within a rule-set version.";
        return null;
    }

    private static ComplianceObligationDto ToDto(ObligationState state, string clientName) => new(
        state.Id,
        state.ClientId,
        clientName,
        state.Code,
        state.Name,
        state.Authority,
        state.PeriodStartUtc,
        state.PeriodEndUtc,
        state.DueDateUtc,
        state.WorkflowStatus,
        state.WorkflowStatus,
        state.PreparationStatus,
        state.ReviewStatus,
        state.SubmissionStatus,
        state.SubmittedAtUtc,
        state.SubmissionReference,
        state.AmountPayable,
        state.AmountRefundable,
        state.PaymentRequired,
        state.PaymentStatus,
        state.PaidAtUtc,
        state.PaymentReference,
        state.RequiredDocumentCategories.Count,
        state.EvidenceFound,
        state.MissingEvidenceCategories,
        state.ResponsibleAccountantId,
        state.RuleVersion,
        state.CreatedReason,
        state.CreatedAtUtc,
        state.UpdatedAtUtc);

    private static string ProfileKey(Guid clientId) => $"{ProfilePrefix}{clientId:N}";
    private static string ObligationKey(Guid obligationId) => $"{ObligationPrefix}{obligationId:N}";

    private sealed class ObligationState
    {
        public Guid Id { get; set; }
        public Guid ClientId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Authority { get; set; } = string.Empty;
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }
        public DateTime? DueDateUtc { get; set; }
        public string WorkflowStatus { get; set; } = "waiting_for_client";
        public string PreparationStatus { get; set; } = "not_started";
        public string ReviewStatus { get; set; } = "not_started";
        public string SubmissionStatus { get; set; } = "not_submitted";
        public DateTime? SubmittedAtUtc { get; set; }
        public string? SubmissionReference { get; set; }
        public decimal? AmountPayable { get; set; }
        public decimal? AmountRefundable { get; set; }
        public bool PaymentRequired { get; set; }
        public string PaymentStatus { get; set; } = "not_required";
        public DateTime? PaidAtUtc { get; set; }
        public string? PaymentReference { get; set; }
        public decimal? AmountPaid { get; set; }
        public List<string> RequiredDocumentCategories { get; set; } = [];
        public List<string> MissingEvidenceCategories { get; set; } = [];
        public int EvidenceFound { get; set; }
        public Guid? ResponsibleAccountantId { get; set; }
        public Guid? EvidenceRequestId { get; set; }
        public bool NotApplicable { get; set; }
        public string? NotApplicableReason { get; set; }
        public string RuleVersion { get; set; } = string.Empty;
        public string CreatedReason { get; set; } = string.Empty;
        public string? LastNote { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
