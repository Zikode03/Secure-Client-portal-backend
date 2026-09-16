using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Compliance;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;

public sealed class ComplianceMonitoringService(PortalDbContext db, ICipcAuthorityClient? cipc = null) : IComplianceMonitoringService
{
    private sealed record Definition(string Code, string Source, string Name, string Description);
    private static readonly Definition[] Catalogue =
    [
        new("cipc_registration", "CIPC", "Company registration", "Registration and enterprise status only; not overall company compliance."),
        new("cipc_annual_returns", "CIPC", "Annual returns", "Annual-return standing from CIPC when the authorised API mapping is configured; otherwise use a manual accountant check."),
        new("cipc_beneficial_ownership", "CIPC", "Beneficial ownership", "Beneficial-ownership standing from CIPC when the authorised API mapping is configured; otherwise use a manual accountant check."),
        new("sars_tcs", "SARS", "Tax compliance status", "TCS verification requires taxpayer authorisation. Automated access is not approved."),
        new("csd_registration", "CSD", "Supplier registration", "Supplier registration only; not a guarantee of tender eligibility or all underlying checks.")
    ];

    private static bool CanManage(ClaimsPrincipal user) => user.GetUserId() is not null && (user.IsAdmin() || user.IsAccountant());
    private async Task<bool> CanRead(Guid id, ClaimsPrincipal user, CancellationToken ct) =>
        user.Identity?.IsAuthenticated == true && (await user.GetAccessibleClientIdsAsync(db, ct)).Contains(id);

    public async Task<ServiceResult<ComplianceMonitoringResponse>> GetAsync(Guid clientId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await CanRead(clientId, user, ct)) return ServiceResult<ComplianceMonitoringResponse>.ForbiddenResult();
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clientId, ct);
        if (client is null) return ServiceResult<ComplianceMonitoringResponse>.NotFoundResult();
        var profile = await db.ComplianceMonitoringProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId, ct);
        var settings = await db.ComplianceCheckSettings.AsNoTracking().Where(x => x.ClientId == clientId).ToListAsync(ct);
        var checks = new List<MonitoringCheckResponse>();
        foreach (var definition in Catalogue)
        {
            var setting = settings.SingleOrDefault(x => x.CheckCode == definition.Code);
            var latest = await db.ComplianceVerifications.AsNoTracking().Where(x => x.ClientId == clientId && x.CheckCode == definition.Code)
                .OrderByDescending(x => x.RecordedAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            var result = latest is null ? null : await MapAsync(latest, client, profile, ct);
            var status = result is null ? "not_checked" : !result.MatchesCurrentIdentifiers ? "identifiers_changed"
                : result.ReviewAfterUtc <= DateTime.UtcNow ? "stale"
                : result.Method == "authority_verified" ? "authority_verified" : "accountant_confirmed";
            var connection = definition.Source == "CIPC" && cipc?.IsConfigured(definition.Code) == true ? "connected" : "not_connected";
            checks.Add(new(definition.Code, definition.Source, definition.Name, definition.Description,
                setting?.Applicability ?? "undecided", setting?.Reason ?? "", connection, status, result));
        }
        return ServiceResult<ComplianceMonitoringResponse>.Success(new(clientId, client.Name, profile?.Version ?? Guid.Empty,
            client.RegistrationNumber, client.TaxNumber, profile?.CsdSupplierNumber ?? "", CanManage(user), checks));
    }

    public async Task<ServiceResult<ComplianceMonitoringResponse>> UpdateAsync(Guid clientId, UpdateComplianceMonitoringRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!CanManage(user) || !await CanRead(clientId, user, ct)) return ServiceResult<ComplianceMonitoringResponse>.ForbiddenResult();
        if (!IdentifierValid(request.RegistrationNumber) || !IdentifierValid(request.TaxNumber) || !IdentifierValid(request.CsdSupplierNumber))
            return Error("Identifiers must be at most 100 characters, using letters, numbers, spaces, slashes or hyphens. Do not enter passwords or PINs.");
        if (request.Checks is null || request.Checks.Count != Catalogue.Length || request.Checks.Any(x => x is null) ||
            request.Checks.Select(x => x.CheckCode).Distinct().Count() != Catalogue.Length ||
            request.Checks.Any(x => !Catalogue.Any(d => d.Code == x.CheckCode) ||
                x.Applicability is not ("undecided" or "applies" or "not_applicable") || x.Reason is null || x.Reason.Length > 500 ||
                (x.Applicability == "not_applicable" && string.IsNullOrWhiteSpace(x.Reason))))
            return Error("Supply each supported check once, with a valid applicability and a reason when it does not apply (maximum 500 characters).");
        var client = await db.Clients.SingleOrDefaultAsync(x => x.Id == clientId, ct);
        if (client is null) return ServiceResult<ComplianceMonitoringResponse>.NotFoundResult();
        var profile = await db.ComplianceMonitoringProfiles.SingleOrDefaultAsync(x => x.ClientId == clientId, ct);
        if ((profile?.Version ?? Guid.Empty) != request.Version) return Conflict();
        if (profile is null) { profile = ComplianceMonitoringProfile.Create(clientId); db.ComplianceMonitoringProfiles.Add(profile); }
        profile.Update(request.CsdSupplierNumber.Trim().ToUpperInvariant());
        client.UpdateComplianceIdentifiers(request.RegistrationNumber.Trim().ToUpperInvariant(), request.TaxNumber.Trim());
        var settings = await db.ComplianceCheckSettings.Where(x => x.ClientId == clientId).ToListAsync(ct);
        foreach (var input in request.Checks)
        {
            var setting = settings.SingleOrDefault(x => x.CheckCode == input.CheckCode);
            if (setting is null) { setting = ComplianceCheckSetting.Create(clientId, input.CheckCode); db.ComplianceCheckSettings.Add(setting); }
            setting.Update(input.Applicability, input.Reason.Trim());
        }
        db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(), user.GetUserId(), user.IsAdmin() ? "admin" : "accountant",
            "compliance.monitoring_configured", "client", clientId, clientId,
            JsonSerializer.Serialize(new { checks = request.Checks.Select(x => new { x.CheckCode, x.Applicability }) })));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return Conflict(); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return Error("Could not save monitoring setup. Refresh and try again."); }
        return await GetAsync(clientId, user, ct);
    }

    public async Task<ServiceResult<ComplianceMonitoringResponse>> RecordManualAsync(Guid clientId, RecordManualVerificationRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!CanManage(user) || !await CanRead(clientId, user, ct)) return ServiceResult<ComplianceMonitoringResponse>.ForbiddenResult();
        if (!Catalogue.Any(x => x.Code == request.CheckCode) || request.Outcome is not ("pass" or "fail" or "unknown") ||
            string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Length > 500 ||
            request.CheckedAtUtc.Kind != DateTimeKind.Utc || request.ReviewAfterUtc.Kind != DateTimeKind.Utc ||
            request.CheckedAtUtc < new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) || request.CheckedAtUtc > DateTime.UtcNow ||
            request.ReviewAfterUtc <= request.CheckedAtUtc)
            return Error("Provide a supported check, outcome, evidence reference and UTC check/review dates. The check cannot be in the future and the review date must follow it.");
        var profile = await db.ComplianceMonitoringProfiles.SingleOrDefaultAsync(x => x.ClientId == clientId, ct);
        if (profile is null || profile.Version != request.Version) return Conflict();
        var setting = await db.ComplianceCheckSettings.SingleOrDefaultAsync(x => x.ClientId == clientId && x.CheckCode == request.CheckCode, ct);
        if (setting?.Applicability != "applies") return Error("Confirm that this check applies before recording a manual verification.");
        var client = await db.Clients.SingleAsync(x => x.Id == clientId, ct);
        var identifier = IdentifierFor(request.CheckCode, client, profile);
        if (string.IsNullOrWhiteSpace(identifier)) return Error("Save the business identifier for this authority before recording a verification.");
        var entry = ComplianceVerification.RecordManual(clientId, request.CheckCode, request.Outcome,
            request.EvidenceReference.Trim(), request.CheckedAtUtc, request.ReviewAfterUtc, user.GetUserId()!.Value, Fingerprint(identifier));
        db.ComplianceVerifications.Add(entry);
        profile.Update(profile.CsdSupplierNumber);
        db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(), user.GetUserId(), user.IsAdmin() ? "admin" : "accountant",
            "compliance.manual_verification_recorded", "compliance_verification", entry.Id, clientId,
            JsonSerializer.Serialize(new { request.CheckCode, request.Outcome, method = "accountant_confirmed" })));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return Conflict(); }
        return await GetAsync(clientId, user, ct);
    }

    public async Task<ServiceResult<ComplianceMonitoringResponse>> VerifyCipcAsync(Guid clientId, RunAuthorityVerificationRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!CanManage(user) || !await CanRead(clientId, user, ct)) return ServiceResult<ComplianceMonitoringResponse>.ForbiddenResult();
        if (!request.CheckCode.StartsWith("cipc_", StringComparison.Ordinal) || !Catalogue.Any(x => x.Code == request.CheckCode))
            return Error("Only supported CIPC checks can use the authority connector.");
        if (cipc is null || !cipc.IsConfigured(request.CheckCode))
            return Error("CIPC integration is not configured for this check. Use the manual accountant-confirmed workflow until the authorised API subscription is configured.", 503);

        var profile = await db.ComplianceMonitoringProfiles.SingleOrDefaultAsync(x => x.ClientId == clientId, ct);
        if (profile is null || profile.Version != request.Version) return Conflict();
        var setting = await db.ComplianceCheckSettings.SingleOrDefaultAsync(x => x.ClientId == clientId && x.CheckCode == request.CheckCode, ct);
        if (setting?.Applicability != "applies") return Error("Confirm that this CIPC check applies before running authority verification.");
        var client = await db.Clients.SingleOrDefaultAsync(x => x.Id == clientId, ct);
        if (client is null) return ServiceResult<ComplianceMonitoringResponse>.NotFoundResult();
        if (string.IsNullOrWhiteSpace(client.RegistrationNumber)) return Error("Save the company registration number before running a CIPC verification.");

        var providerResult = await cipc.VerifyAsync(request.CheckCode, client.RegistrationNumber, ct);
        if (!providerResult.Success)
            return Error(providerResult.Error ?? "CIPC verification is unavailable. No result was saved.", 503);
        if (providerResult.Outcome is not ("pass" or "fail"))
            return Error("CIPC returned an unsupported result. No result was saved.", 503);

        var entry = ComplianceVerification.RecordAuthority(clientId, request.CheckCode, providerResult.Outcome,
            providerResult.EvidenceReference, providerResult.CheckedAtUtc, providerResult.ReviewAfterUtc,
            user.GetUserId()!.Value, Fingerprint(client.RegistrationNumber));
        db.ComplianceVerifications.Add(entry);
        profile.Update(profile.CsdSupplierNumber);
        db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(), user.GetUserId(), user.IsAdmin() ? "admin" : "accountant",
            "compliance.authority_verification_recorded", "compliance_verification", entry.Id, clientId,
            JsonSerializer.Serialize(new { request.CheckCode, providerResult.Outcome, source = "CIPC", method = "authority_verified" })));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return Conflict(); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return Error("The CIPC result was received but could not be saved. Refresh before trying again.", 500); }
        return await GetAsync(clientId, user, ct);
    }

    public async Task<ServiceResult<IReadOnlyList<VerificationResponse>>> HistoryAsync(Guid clientId, string? checkCode, int page, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await CanRead(clientId, user, ct)) return ServiceResult<IReadOnlyList<VerificationResponse>>.ForbiddenResult();
        if (page < 1 || page > 10000 || (checkCode is not null && !Catalogue.Any(x => x.Code == checkCode)))
            return ServiceResult<IReadOnlyList<VerificationResponse>>.ErrorResult("Invalid check or page.");
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clientId, ct);
        if (client is null) return ServiceResult<IReadOnlyList<VerificationResponse>>.NotFoundResult();
        var profile = await db.ComplianceMonitoringProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId, ct);
        var rows = await db.ComplianceVerifications.AsNoTracking().Where(x => x.ClientId == clientId && (checkCode == null || x.CheckCode == checkCode))
            .OrderByDescending(x => x.RecordedAtUtc).ThenByDescending(x => x.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        var result = new List<VerificationResponse>();
        foreach (var row in rows) result.Add(await MapAsync(row, client, profile, ct));
        return ServiceResult<IReadOnlyList<VerificationResponse>>.Success(result);
    }

    private async Task<VerificationResponse> MapAsync(ComplianceVerification row, Client client, ComplianceMonitoringProfile? profile, CancellationToken ct)
    {
        var name = await db.Users.Where(x => x.Id == row.RecordedByUserId).Select(x => x.FullName).FirstOrDefaultAsync(ct) ?? "Former team member";
        return new(row.Id, row.CheckCode, row.Method, row.Outcome, row.EvidenceReference, DateTime.SpecifyKind(row.CheckedAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(row.RecordedAtUtc, DateTimeKind.Utc), DateTime.SpecifyKind(row.ReviewAfterUtc, DateTimeKind.Utc), row.RecordedByUserId, name,
            row.IdentifierFingerprint == Fingerprint(IdentifierFor(row.CheckCode, client, profile)));
    }
    private static string IdentifierFor(string code, Client client, ComplianceMonitoringProfile? profile) =>
        code.StartsWith("cipc_", StringComparison.Ordinal) ? client.RegistrationNumber : code == "sars_tcs" ? client.TaxNumber : profile?.CsdSupplierNumber ?? "";
    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())));
    private static bool IdentifierValid(string? value) => value is not null && value.Length <= 100 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '/' or '-');
    private static ServiceResult<ComplianceMonitoringResponse> Error(string message, int statusCode = 400) => ServiceResult<ComplianceMonitoringResponse>.ErrorResult(message, statusCode: statusCode);
    private static ServiceResult<ComplianceMonitoringResponse> Conflict() => ServiceResult<ComplianceMonitoringResponse>.ErrorResult("This setup changed. Reload before saving again.", statusCode: 409);
}
