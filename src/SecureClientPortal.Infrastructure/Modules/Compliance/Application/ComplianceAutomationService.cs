using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Compliance;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;

public sealed class ComplianceAutomationService(PortalDbContext db, IComplianceService evidenceService) : IComplianceAutomationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)
        ?? throw new InvalidOperationException("Invalid persisted compliance data.");
    private static bool Staff(ClaimsPrincipal user) => user.IsAdmin() || user.IsAccountant();
    private static bool PortalUser(ClaimsPrincipal user) => Staff(user) || user.IsClient();
    private async Task<bool> Access(Guid id, ClaimsPrincipal user, CancellationToken ct) =>
        PortalUser(user) && (await user.GetAccessibleClientIdsAsync(db, ct)).Contains(id);
    private static ServiceResult<T> Error<T>(string message, int status = 400) => ServiceResult<T>.ErrorResult(message, statusCode: status);

    public async Task<ServiceResult<ComplianceRuleSet>> GetRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (!PortalUser(user)) return ServiceResult<ComplianceRuleSet>.ForbiddenResult();
        return ServiceResult<ComplianceRuleSet>.Success(await Rules(ct));
    }

    private async Task<ComplianceRuleSet> Rules(CancellationToken ct)
    {
        var row = await db.ComplianceAutomationConfigurations.FindAsync(["rules"], ct);
        return row is null ? StarterRules() : Decode<ComplianceRuleSet>(row.PayloadJson);
    }

    // These are configurable workflow templates, not verified statutory deadlines.
    // Unconfirmed registrations never generate work. Administrators must verify due rules.
    private static ComplianceRuleSet StarterRules()
    {
        const string version = "unconfigured-v1";
        var since = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ComplianceRuleDefinition Rule(string code, string name, string authority, string field, int cadence, params string[] documents) =>
            new(code, name, authority, code, cadence, 1, null, field, code != "CSD", false, documents, since, null, version);
        return new(version, [
            Rule("VAT201", "VAT return", "SARS", "vatRegistered", 2, "bank_statement", "invoices"),
            Rule("EMP201", "Employer declaration", "SARS", "payeRegistered", 1, "payroll"),
            Rule("UIF", "UIF declaration", "UIF", "uifRegistered", 1, "payroll"),
            Rule("COIDA", "Compensation Fund return", "Compensation Fund", "coidaRegistered", 12, "payroll"),
            Rule("IRP6", "Provisional tax", "SARS", "provisionalTaxpayer", 6, "financial_statements"),
            Rule("ITR14", "Company income tax return", "SARS", "companyTaxRegistered", 12, "financial_statements"),
            Rule("CIPC", "Company annual return", "CIPC", "cipcRegistered", 12),
            Rule("CSD", "Central Supplier Database registration", "CSD", "governmentSupplier", 0)
        ], since);
    }

    private static bool? Applies(ClientComplianceProfile p, string field) => field switch
    {
        "vatRegistered" => p.VatRegistered, "payeRegistered" => p.PayeRegistered,
        "uifRegistered" => p.UifRegistered, "coidaRegistered" => p.CoidaRegistered,
        "provisionalTaxpayer" => p.ProvisionalTaxpayer, "companyTaxRegistered" => p.CompanyTaxRegistered,
        "cipcRegistered" => p.CipcRegistered, "governmentSupplier" => p.GovernmentSupplier,
        "csdRegistered" => p.CsdRegistered, _ => null
    };
    private static readonly string[] Fields = ["vatRegistered", "payeRegistered", "uifRegistered", "coidaRegistered",
        "provisionalTaxpayer", "companyTaxRegistered", "cipcRegistered", "governmentSupplier", "csdRegistered"];

    public async Task<ServiceResult<ComplianceRuleSet>> UpdateRulesAsync(UpdateComplianceRulesRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!user.IsAdmin()) return ServiceResult<ComplianceRuleSet>.ForbiddenResult();
        if (string.IsNullOrWhiteSpace(request.Version) || request.Version.Length > 100 || request.Rules is null
            || request.Rules.Length is < 1 or > 50)
            return Error<ComplianceRuleSet>("Provide a version and between 1 and 50 rules.");
        if (request.Rules.Any(r => r is null || string.IsNullOrWhiteSpace(r.Code) || r.Code.Length > 50
            || !System.Text.RegularExpressions.Regex.IsMatch(r.Code, "^[A-Z0-9_-]+$")
            || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200 || string.IsNullOrWhiteSpace(r.Authority)
            || r.Authority.Length > 100 || string.IsNullOrWhiteSpace(r.CategoryCode) || r.CategoryCode.Length > 50
            || !Fields.Contains(r.ApplicabilityField) || r.DueOffsetMonths is < 0 or > 24
            || (r.RequiresPayment && !r.RequiresSubmission)
            || r.DueDayOfMonth is < 1 or > 31 || r.CadenceMonths is < 0 or > 12
            || (r.Code != "CSD" && (r.CadenceMonths == 0 || 12 % r.CadenceMonths != 0))
            || (r.Code == "CSD" && (r.CadenceMonths != 0 || r.RequiresSubmission || r.RequiresPayment
                || r.DueDayOfMonth != null || r.ApplicabilityField != "governmentSupplier"))
            || r.RequiredDocumentCategories is null || r.RequiredDocumentCategories.Length > 30
            || r.RequiredDocumentCategories.Any(c => string.IsNullOrWhiteSpace(c) || c.Length > 100)
            || r.EffectiveFromUtc == default || r.EffectiveToUtc <= r.EffectiveFromUtc))
            return Error<ComplianceRuleSet>("Invalid rule, applicability, period, document category or deadline.");
        if (request.Rules.Select(r => r.Code).Distinct().Count() != request.Rules.Length)
            return Error<ComplianceRuleSet>("Rule codes must be unique.");
        var version = request.Version.Trim();
        var key = "rules-version:" + version;
        if (key.Length > 100) return Error<ComplianceRuleSet>("Version must be at most 86 characters.");
        if (await db.ComplianceAutomationConfigurations.AnyAsync(x => x.Key == key, ct))
            return Error<ComplianceRuleSet>("This rule version already exists. Use a new version.", 409);
        var next = new ComplianceRuleSet(version, request.Rules.Select(r => r with
        {
            Version = version,
            RequiredDocumentCategories = r.RequiredDocumentCategories.Select(Normalize).Distinct().ToArray()
        }).ToArray(), DateTime.UtcNow);
        await SetConfiguration("rules", null, next, ct);
        await SetConfiguration(key, null, next, ct);
        Audit(user, "rules_updated", Guid.NewGuid(), null, new { version });
        return await Save(next, ct);
    }

    public async Task<ServiceResult<ClientComplianceProfile>> GetProfileAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await Access(clientId, user, ct)) return ServiceResult<ClientComplianceProfile>.ForbiddenResult();
        return ServiceResult<ClientComplianceProfile>.Success(await Profile(clientId, ct));
    }
    private async Task<ClientComplianceProfile> Profile(Guid id, CancellationToken ct)
    {
        var row = await db.ComplianceAutomationConfigurations.FindAsync(["profile:" + id], ct);
        return row is null ? new ClientComplianceProfile { ClientId = id } : Decode<ClientComplianceProfile>(row.PayloadJson);
    }
    public async Task<ServiceResult<ClientComplianceProfile>> UpdateProfileAsync(Guid clientId, ClientComplianceProfile request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Staff(user) || !await Access(clientId, user, ct)) return ServiceResult<ClientComplianceProfile>.ForbiddenResult();
        if (!new[] { 1, 2, 3, 6, 12 }.Contains(request.VatCycleMonths) || request.VatAnchorMonth is < 1 or > 12
            || request.FinancialYearEndMonth is < 1 or > 12 || request.CsdSupplierNumber?.Length > 100)
            return Error<ClientComplianceProfile>("Provide valid cycle months, year-end month and supplier number.");
        var next = request with { ClientId = clientId, UpdatedAtUtc = DateTime.UtcNow, CsdSupplierNumber = request.CsdSupplierNumber?.Trim() };
        await SetConfiguration("profile:" + clientId, clientId, next, ct);
        Audit(user, "profile_updated", clientId, clientId, new { clientId });
        return await Save(next, ct);
    }

    public async Task<ServiceResult<IReadOnlyList<ComplianceObligationResponse>>> GetObligationsAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!PortalUser(user)) return ServiceResult<IReadOnlyList<ComplianceObligationResponse>>.ForbiddenResult();
        var allowed = await user.GetAccessibleClientIdsAsync(db, ct);
        if (clientId.HasValue && !allowed.Contains(clientId.Value))
            return ServiceResult<IReadOnlyList<ComplianceObligationResponse>>.ForbiddenResult();
        var rows = await db.ComplianceObligations.AsNoTracking()
            .Where(x => allowed.Contains(x.ClientId) && (!clientId.HasValue || x.ClientId == clientId))
            .OrderByDescending(x => x.PeriodStartUtc).ToListAsync(ct);
        var result = new List<ComplianceObligationResponse>();
        foreach (var row in rows) result.Add(await Refresh(row, ct));
        return ServiceResult<IReadOnlyList<ComplianceObligationResponse>>.Success(result);
    }

    public async Task<ServiceResult<ComplianceAutomationRunResult>> RunAsync(Guid? clientId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Staff(user)) return ServiceResult<ComplianceAutomationRunResult>.ForbiddenResult();
        var allowed = await user.GetAccessibleClientIdsAsync(db, ct);
        if (clientId.HasValue && !allowed.Contains(clientId.Value)) return ServiceResult<ComplianceAutomationRunResult>.ForbiddenResult();
        var clients = await db.Clients.Where(x => allowed.Contains(x.Id) && (!clientId.HasValue || x.Id == clientId) && x.Status == "active").ToListAsync(ct);
        var now = DateTime.UtcNow;
        var rules = await Rules(ct);
        var warnings = new HashSet<string> { "This run reconciles local workflows only; it does not check or submit to government systems." };
        var created = 0; var refreshed = 0;
        foreach (var client in clients)
        {
            var profile = await Profile(client.Id, ct);
            if (!profile.UpdatedAtUtc.HasValue) warnings.Add(client.Name + ": confirm the compliance profile first.");
            foreach (var rule in rules.Rules.Where(r => r.EffectiveFromUtc <= now && (!r.EffectiveToUtc.HasValue || r.EffectiveToUtc >= now)))
            {
                if (Applies(profile, rule.ApplicabilityField) != true) continue;
                // The current profile does not collect incorporation anniversaries or COIDA
                // assessment-year settings. Never fabricate those periods from financial year end.
                if (rule.Code is "CIPC" or "COIDA")
                {
                    warnings.Add(rule.Code + ": period-specific registration/assessment dates are not configured; no obligation generated.");
                    continue;
                }
                var (start, end) = Period(profile, rule, now);
                var existing = await db.ComplianceObligations.FirstOrDefaultAsync(x =>
                    x.ClientId == client.Id && x.Code == rule.Code && x.PeriodStartUtc == start, ct);
                if (existing is not null) continue;
                DateTime? due = null;
                if (rule.Code != "CSD")
                {
                    if (rule.DueDayOfMonth is int day)
                    {
                        var month = new DateTime(end.Year, end.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(rule.DueOffsetMonths);
                        due = month.AddDays(Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
                    }
                    else warnings.Add(rule.Code + ": deadline is not configured.");
                }
                var category = db.ComplianceCategories.Local.FirstOrDefault(x => x.Code == rule.CategoryCode)
                    ?? await db.ComplianceCategories.FirstOrDefaultAsync(x => x.Code == rule.CategoryCode, ct);
                if (category is null)
                {
                    category = ComplianceCategory.Create(Guid.NewGuid(), rule.Name, "Compliance workflow evidence", rule.CategoryCode);
                    db.ComplianceCategories.Add(category);
                }
                var item = ComplianceItem.Create(Guid.NewGuid(), client.Id, category.Id,
                    rule.Name + " " + start.ToString("yyyy-MM"), ComplianceItemStatus.Missing,
                    client.AssignedAccountantId, ComplianceRiskLevel.Medium, null, due, null);
                db.ComplianceItems.Add(item);
                var row = new ComplianceObligation
                {
                    Id = Guid.NewGuid(), ClientId = client.Id, Code = rule.Code,
                    PeriodStartUtc = start, ComplianceItemId = item.Id, RuleJson = Encode(rule)
                };
                var state = new ComplianceObligationResponse
                {
                    Id = row.Id, ClientId = client.Id, ClientName = client.Name, Code = rule.Code, Name = rule.Name,
                    Authority = rule.Authority, PeriodStartUtc = start, PeriodEndUtc = end, DueDateUtc = due,
                    ResponsibleAccountantId = client.AssignedAccountantId, RuleVersion = rule.Version,
                    SubmissionStatus = rule.RequiresSubmission ? "not_submitted" : "not_required",
                    PaymentRequired = rule.RequiresPayment, PaymentStatus = rule.RequiresPayment ? "outstanding" : "not_required",
                    CreatedReason = "Confirmed " + rule.ApplicabilityField + "; local workflow only, not an authority compliance conclusion.",
                    CreatedAtUtc = now, UpdatedAtUtc = now
                };
                row.StateJson = Encode(state);
                db.ComplianceObligations.Add(row);
                created++;
                Audit(user, "obligation_created", row.Id, client.Id, new { row.ComplianceItemId, rule.Version });
            }
            var persisted = await db.ComplianceObligations.Where(x => x.ClientId == client.Id).ToListAsync(ct);
            var rows = persisted.Concat(db.ComplianceObligations.Local.Where(x => x.ClientId == client.Id)).DistinctBy(x => x.Id);
            foreach (var row in rows)
            {
                row.StateJson = Encode((await Refresh(row, ct)) with { UpdatedAtUtc = now });
                refreshed++;
            }
        }
        // Request/notification creation is deliberately not simulated.
        warnings.Add("Missing evidence is listed here; this run does not send document requests or notifications.");
        Audit(user, "run", Guid.NewGuid(), clientId, new { created, refreshed });
        return await Save(new ComplianceAutomationRunResult(now, clients.Count, created, refreshed, 0, 0, warnings.ToArray()), ct);
    }

    private static (DateTime Start, DateTime End) Period(ClientComplianceProfile profile, ComplianceRuleDefinition rule, DateTime now)
    {
        if (rule.Code == "CSD")
        {
            var standing = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (standing, standing); // stable identity; not a recurring return
        }
        var cadence = rule.Code == "VAT201" ? profile.VatCycleMonths : rule.CadenceMonths;
        var anchor = rule.Code == "VAT201" ? profile.VatAnchorMonth : cadence > 1 ? profile.FinancialYearEndMonth : 1;
        var endMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        while ((endMonth.Month - anchor + 12) % cadence != 0) endMonth = endMonth.AddMonths(-1);
        return (endMonth.AddMonths(1 - cadence), endMonth.AddMonths(1).AddTicks(-1));
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
    private async Task<ComplianceObligationResponse> Refresh(ComplianceObligation row, CancellationToken ct)
    {
        var state = Decode<ComplianceObligationResponse>(row.StateJson);
        var rule = Decode<ComplianceRuleDefinition>(row.RuleJson);
        var client = await db.Clients.AsNoTracking().FirstAsync(x => x.Id == row.ClientId, ct);
        var categories = rule.RequiredDocumentCategories.Select(Normalize).Distinct().ToArray();
        var startMonth = state.PeriodStartUtc.Year * 12 + state.PeriodStartUtc.Month;
        var endMonth = state.PeriodEndUtc.Year * 12 + state.PeriodEndUtc.Month;
        var found = await (from doc in db.Documents
                           join pack in db.MonthlyPacks on doc.MonthlyPackId equals pack.Id
                           where doc.ClientId == row.ClientId && pack.ClientId == row.ClientId
                               && doc.Status == "accepted" && doc.StorageKey != null
                               && pack.Year * 12 + pack.Month >= startMonth && pack.Year * 12 + pack.Month <= endMonth
                           select doc.Category).Distinct().ToListAsync(ct);
        var missing = categories.Except(found.Select(Normalize)).ToList();
        var required = categories.Length;
        if (rule.Code == "CSD")
        {
            required++;
            if (!await db.ComplianceEvidenceVersions.AnyAsync(x => x.ComplianceItemId == row.ComplianceItemId
                && x.ClientId == row.ClientId && x.IsCurrentVersion, ct)) missing.Add("csd_registration_evidence");
        }
        state = state with { ClientName = client.Name, ResponsibleAccountantId = client.AssignedAccountantId,
            EvidenceRequired = required, EvidenceFound = required - missing.Count, MissingEvidenceCategories = missing.ToArray() };
        return Status(state);
    }

    private static ComplianceObligationResponse Status(ComplianceObligationResponse s)
    {
        var ready = s.NotApplicableReason is not null ? "not_applicable"
            : s.MissingEvidenceCategories.Length > 0 ? "waiting_for_client"
            : s.PreparationStatus != "complete" ? (s.PreparationStatus == "in_progress" ? "in_preparation" : "ready_to_prepare")
            : s.ReviewStatus != "approved" ? "ready_for_review"
            : s.SubmissionStatus == "not_submitted" ? "ready_to_file"
            : s.PaymentRequired && s.PaymentStatus != "paid" ? "payment_outstanding" : "complete";
        return s with { Readiness = ready, WorkflowStatus = ready is not ("complete" or "not_applicable")
            && s.DueDateUtc?.Date < DateTime.UtcNow.Date ? "overdue" : ready };
    }

    public Task<ServiceResult<ComplianceObligationResponse>> PreparationAsync(Guid id, PreparationRequest r, ClaimsPrincipal u, CancellationToken ct) =>
        Change(id, u, "preparation", r, s =>
        {
            if (s.SubmissionStatus == "submitted") return (s, "A filed obligation cannot be prepared again.");
            if (r.Complete && s.MissingEvidenceCategories.Length > 0) return (s, "Required evidence is still missing.");
            return (s with { PreparationStatus = r.Complete ? "complete" : "in_progress", ReviewStatus = "not_started" }, null);
        }, ct);
    public Task<ServiceResult<ComplianceObligationResponse>> ReviewAsync(Guid id, ReviewObligationRequest r, ClaimsPrincipal u, CancellationToken ct) =>
        Change(id, u, "review", r, s =>
        {
            if (s.SubmissionStatus == "submitted") return (s, "A filed obligation cannot be reviewed again.");
            if (s.PreparationStatus != "complete" || s.MissingEvidenceCategories.Length > 0) return (s, "Complete preparation and evidence before review.");
            return (s with { ReviewStatus = r.Approved ? "approved" : "changes_required",
                PreparationStatus = r.Approved ? "complete" : "in_progress" }, null);
        }, ct);
    public Task<ServiceResult<ComplianceObligationResponse>> SubmissionAsync(Guid id, SubmissionRequest r, ClaimsPrincipal u, CancellationToken ct) =>
        Change(id, u, "submission", r, s =>
        {
            if (s.SubmissionStatus != "not_submitted" || s.ReviewStatus != "approved" || s.MissingEvidenceCategories.Length > 0)
                return (s, "Only reviewed, evidence-ready obligations awaiting filing can be submitted.");
            if (!ValidReference(r.SubmissionReference) || !ValidDate(r.SubmittedAtUtc) || r.AmountPayable < 0 || r.AmountRefundable < 0
                || r.AmountPayable > 0 && r.AmountRefundable > 0 || r.AmountPayable > 0 && !r.PaymentRequired
                || r.PaymentRequired && r.AmountPayable is not > 0 || s.PaymentRequired && !r.PaymentRequired)
                return (s, "Provide a real submission date/reference and consistent non-negative amounts; required payments cannot be waived.");
            return (s with { SubmissionStatus = "submitted", SubmittedAtUtc = r.SubmittedAtUtc.ToUniversalTime(),
                SubmissionReference = r.SubmissionReference.Trim(), AmountPayable = r.AmountPayable, AmountRefundable = r.AmountRefundable,
                PaymentRequired = r.PaymentRequired, PaymentStatus = r.PaymentRequired ? "outstanding" : "not_required" }, null);
        }, ct);
    public Task<ServiceResult<ComplianceObligationResponse>> PaymentAsync(Guid id, PaymentRequest r, ClaimsPrincipal u, CancellationToken ct) =>
        Change(id, u, "payment", r, s =>
        {
            if (s.SubmissionStatus != "submitted" || !s.PaymentRequired || s.PaymentStatus == "paid")
                return (s, "Record the submission first; payment must still be outstanding.");
            if (!ValidReference(r.PaymentReference) || !ValidDate(r.PaidAtUtc) || r.AmountPaid <= 0
                || s.AmountPayable is null || r.AmountPaid < s.AmountPayable)
                return (s, "Provide the payment date/reference and full amount paid. A partial payment cannot close the obligation.");
            return (s with { PaidAtUtc = r.PaidAtUtc.ToUniversalTime(), PaymentReference = r.PaymentReference.Trim(),
                AmountPaid = r.AmountPaid, PaymentStatus = "paid" }, null);
        }, ct);
    public Task<ServiceResult<ComplianceObligationResponse>> NotApplicableAsync(Guid id, NotApplicableRequest r, ClaimsPrincipal u, CancellationToken ct) =>
        Change(id, u, "not_applicable", r, s =>
        {
            if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 1000 || s.SubmissionStatus == "submitted")
                return (s, "Provide a reason; submitted obligations cannot be marked not applicable.");
            return (s with { NotApplicableReason = r.Reason.Trim() }, null);
        }, ct);
    private static bool ValidReference(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200;
    // The UI records calendar dates at noon UTC; compare dates to avoid rejecting today's entries.
    private static bool ValidDate(DateTime value) => value != default && value.Year >= 2000 && value.ToUniversalTime().Date <= DateTime.UtcNow.Date;

    private async Task<ServiceResult<ComplianceObligationResponse>> Change(Guid id, ClaimsPrincipal user, string action, object request,
        Func<ComplianceObligationResponse, (ComplianceObligationResponse State, string? Error)> change, CancellationToken ct)
    {
        if (!Staff(user)) return ServiceResult<ComplianceObligationResponse>.ForbiddenResult();
        var row = await db.ComplianceObligations.FindAsync([id], ct);
        if (row is null) return ServiceResult<ComplianceObligationResponse>.NotFoundResult();
        if (!await Access(row.ClientId, user, ct)) return ServiceResult<ComplianceObligationResponse>.ForbiddenResult();
        var before = await Refresh(row, ct);
        if (before.NotApplicableReason is not null) return Error<ComplianceObligationResponse>("This obligation is marked not applicable.", 409);
        var (next, error) = change(before);
        if (error is not null) return Error<ComplianceObligationResponse>(error);
        next = Status(next with { UpdatedAtUtc = DateTime.UtcNow });
        row.StateJson = Encode(next);
        Audit(user, action, id, row.ClientId, request);
        return await Save(next, ct);
    }

    public async Task<ServiceResult<ObligationEvidenceResponse>> UploadEvidenceAsync(Guid id, UploadComplianceEvidenceRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var row = await db.ComplianceObligations.FindAsync([id], ct);
        if (row is null) return ServiceResult<ObligationEvidenceResponse>.NotFoundResult();
        if (!await Access(row.ClientId, user, ct)) return ServiceResult<ObligationEvidenceResponse>.ForbiddenResult();
        // Check BOTH identities before delegating: obligation IDs are never legacy item IDs.
        if (!await db.ComplianceItems.AnyAsync(x => x.Id == row.ComplianceItemId && x.ClientId == row.ClientId, ct))
            return Error<ObligationEvidenceResponse>("The obligation evidence link is invalid.", 409);
        if (request.Note?.Length > 2000) return Error<ObligationEvidenceResponse>("Evidence note must be at most 2000 characters.");
        var uploaded = await evidenceService.UploadEvidenceAsync(row.ComplianceItemId.ToString(), request, user, ct);
        if (uploaded.Value is null) return new(default, uploaded.Forbidden, uploaded.NotFound, uploaded.Unauthorized,
            uploaded.Error, uploaded.ErrorCode, uploaded.StatusCode);
        // UploadEvidenceAsync only returns after protected storage/scanning has succeeded.
        // A filing receipt does not satisfy unrelated monthly preparation categories.
        var state = await Refresh(row, ct);
        // Replacing a standing registration report requires a fresh professional review.
        // Filing receipts do not reset an already recorded external submission.
        if (row.Code == "CSD")
            state = Status(state with { PreparationStatus = "not_started", ReviewStatus = "not_started" });
        state = state with { UpdatedAtUtc = DateTime.UtcNow };
        row.StateJson = Encode(state);
        Audit(user, "evidence_linked", row.Id, row.ClientId, new { row.ComplianceItemId, evidenceVersionId = uploaded.Value.Id });
        return await Save(new ObligationEvidenceResponse(state, uploaded.Value), ct);
    }

    public async Task<ServiceResult<IReadOnlyList<ComplianceEvidenceVersionResponse>>> GetEvidenceAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var row = await db.ComplianceObligations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return ServiceResult<IReadOnlyList<ComplianceEvidenceVersionResponse>>.NotFoundResult();
        if (!await Access(row.ClientId, user, ct)) return ServiceResult<IReadOnlyList<ComplianceEvidenceVersionResponse>>.ForbiddenResult();
        if (!await db.ComplianceItems.AnyAsync(x => x.Id == row.ComplianceItemId && x.ClientId == row.ClientId, ct))
            return Error<IReadOnlyList<ComplianceEvidenceVersionResponse>>("The obligation evidence link is invalid.", 409);
        return await evidenceService.GetEvidenceVersionsAsync(row.ComplianceItemId.ToString(), user, ct);
    }

    private async Task SetConfiguration<T>(string key, Guid? clientId, T value, CancellationToken ct)
    {
        var row = await db.ComplianceAutomationConfigurations.FindAsync([key], ct);
        if (row is null)
        {
            row = new ComplianceAutomationConfiguration { Key = key, ClientId = clientId };
            db.ComplianceAutomationConfigurations.Add(row);
        }
        row.PayloadJson = Encode(value);
    }
    private void Audit(ClaimsPrincipal user, string action, Guid id, Guid? clientId, object metadata) =>
        db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(), user.GetUserId(), user.IsAdmin() ? "admin" : user.IsAccountant() ? "accountant" : "client",
            "compliance.automation." + action, "compliance_obligation", id, clientId, Encode(metadata)));
    private async Task<ServiceResult<T>> Save<T>(T result, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return ServiceResult<T>.Success(result); }
        catch (DbUpdateConcurrencyException) { return Error<T>("Compliance data changed. Reload before trying again.", 409); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        { return Error<T>("This record was created by another request. Reload before trying again.", 409); }
    }
}
