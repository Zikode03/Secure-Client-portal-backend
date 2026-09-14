using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Tests;

public sealed class ComplianceMonitoringTests
{
    private static readonly Guid BusinessId = Guid.Parse("b1000000-0000-0000-0000-000000000010");
    private static readonly Guid StaffId = Guid.Parse("a1000000-0000-0000-0000-000000000010");
    private static readonly string[] Codes = ["cipc_registration", "cipc_annual_returns", "cipc_beneficial_ownership", "sars_tcs", "csd_registration"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClaimsPrincipal Actor(string role, Guid? businessId = null, Guid? actorId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, (actorId ?? StaffId).ToString()), new(ClaimTypes.Role, role) };
        if (businessId.HasValue) claims.Add(new("client_id", businessId.Value.ToString()));
        return new(new ClaimsIdentity(claims, "test"));
    }
    private static PortalDbContext Database()
    {
        var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client = Client.Create(BusinessId, "Test Business", "Pty Ltd", "Contact", "contact@example.test", ClientStatus.Active);
        client.AssignAccountant(StaffId);
        db.Clients.Add(client);
        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), StaffId, BusinessId));
        var staff = User.CreateInvited(StaffId, "Accountant", "staff@example.test", UserRole.Accountant, "hash", "[]", null);
        staff.CompleteSetup("Accountant", "hash"); db.Users.Add(staff);
        db.SaveChanges();
        return db;
    }
    private static UpdateComplianceMonitoringRequest Setup(Guid version = default) => new(version, "REG-EXAMPLE", "1234567890", "SUPPLIER-EXAMPLE",
        Codes.Select(code => new CheckApplicabilityRequest(code, code == "cipc_registration" ? "applies" : "undecided", "")).ToArray());
    private static RecordManualVerificationRequest Observation(Guid version, DateTime? checkedAt = null, DateTime? reviewAfter = null) =>
        new(version, "cipc_registration", "pass", "Secure internal evidence ref 42", checkedAt ?? DateTime.UtcNow.AddHours(-1), reviewAfter ?? DateTime.UtcNow.AddDays(7));

    [Fact]
    public async Task NewBusiness_HasNoAssumedApplicabilityOrVerification_AndGetDoesNotWrite()
    {
        await using var db = Database();
        var result = await new ComplianceMonitoringService(db).GetAsync(BusinessId, Actor("client", BusinessId), Ct);
        Assert.NotNull(result.Value); Assert.False(result.Value.CanManage);
        Assert.Equal(5, result.Value.Checks.Count);
        Assert.All(result.Value.Checks, check => { Assert.Equal("undecided", check.Applicability); Assert.Equal("not_checked", check.VerificationStatus); Assert.Equal("not_connected", check.ConnectionStatus); Assert.Null(check.LatestVerification); });
        Assert.Empty(db.ComplianceMonitoringProfiles); Assert.Empty(db.ComplianceVerifications);
    }

    [Fact]
    public async Task Setup_PersistsSharedIdentifiersAndApplicability_WithRedactedAudit()
    {
        await using var db = Database();
        var service = new ComplianceMonitoringService(db);
        var result = await service.UpdateAsync(BusinessId, Setup(), Actor("accountant"), Ct);
        Assert.NotNull(result.Value); Assert.NotEqual(Guid.Empty, result.Value.Version);
        db.ChangeTracker.Clear();
        var reloaded = await service.GetAsync(BusinessId, Actor("client", BusinessId), Ct);
        Assert.Equal("REG-EXAMPLE", reloaded.Value!.RegistrationNumber);
        Assert.Equal("SUPPLIER-EXAMPLE", reloaded.Value.CsdSupplierNumber);
        Assert.Equal("1234567890", (await db.Clients.SingleAsync(Ct)).TaxNumber);
        Assert.Equal("applies", reloaded.Value.Checks[0].Applicability);
        Assert.All(reloaded.Value.Checks, check => Assert.Equal("not_checked", check.VerificationStatus));
        var audit = await db.AuditLogs.SingleAsync(Ct);
        Assert.DoesNotContain("1234567890", audit.MetadataJson); Assert.DoesNotContain("REG-EXAMPLE", audit.MetadataJson);
        Assert.Empty(db.ComplianceVerifications);
    }

    [Fact]
    public async Task ClientsCannotWrite_AndUnassignedStaffCannotReadOrWriteHistory()
    {
        await using var db = Database();
        var service = new ComplianceMonitoringService(db);
        Assert.True((await service.UpdateAsync(BusinessId, Setup(), Actor("client", BusinessId), Ct)).Forbidden);
        Assert.True((await service.RecordManualAsync(BusinessId, Observation(Guid.Empty), Actor("client", BusinessId), Ct)).Forbidden);
        var other = Actor("accountant", actorId: Guid.NewGuid());
        Assert.True((await service.GetAsync(BusinessId, other, Ct)).Forbidden);
        Assert.True((await service.UpdateAsync(BusinessId, Setup(), other, Ct)).Forbidden);
        Assert.True((await service.RecordManualAsync(BusinessId, Observation(Guid.Empty), other, Ct)).Forbidden);
        Assert.True((await service.HistoryAsync(BusinessId, null, 1, other, Ct)).Forbidden);
        Assert.True((await service.HistoryAsync(BusinessId, null, 1, Actor("client", Guid.NewGuid()), Ct)).Forbidden);
        Assert.Empty(db.ComplianceMonitoringProfiles); Assert.Empty(db.ComplianceVerifications);
    }

    [Fact]
    public async Task RejectsMissingReasonsDuplicateChecksAndOversizedIdentifiersBeforeWriting()
    {
        await using var db = Database(); var service = new ComplianceMonitoringService(db);
        var noReason = Setup() with { Checks = Codes.Select(code => new CheckApplicabilityRequest(code, "not_applicable", "")).ToArray() };
        Assert.NotNull((await service.UpdateAsync(BusinessId, noReason, Actor("admin"), Ct)).Error);
        var duplicate = Setup() with { Checks = Enumerable.Repeat(new CheckApplicabilityRequest(Codes[0], "applies", ""), 5).ToArray() };
        Assert.NotNull((await service.UpdateAsync(BusinessId, duplicate, Actor("admin"), Ct)).Error);
        Assert.NotNull((await service.UpdateAsync(BusinessId, Setup() with { TaxNumber = new string('1', 101) }, Actor("admin"), Ct)).Error);
        Assert.Empty(db.ComplianceMonitoringProfiles); Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task StaleSetupCannotOverwriteANewerRevision()
    {
        await using var db = Database(); var service = new ComplianceMonitoringService(db);
        await service.UpdateAsync(BusinessId, Setup(), Actor("admin"), Ct);
        var result = await service.UpdateAsync(BusinessId, Setup() with { RegistrationNumber = "DIFFERENT" }, Actor("admin"), Ct);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("REG-EXAMPLE", (await db.Clients.SingleAsync(Ct)).RegistrationNumber);
    }

    [Fact]
    public async Task ManualObservation_PersistsAsManualOnly_WithImmutableHistoryAndNoConnection()
    {
        await using var db = Database(); var service = new ComplianceMonitoringService(db);
        var setup = (await service.UpdateAsync(BusinessId, Setup(), Actor("accountant"), Ct)).Value!;
        var first = await service.RecordManualAsync(BusinessId, Observation(setup.Version), Actor("accountant"), Ct);
        Assert.NotNull(first.Value);
        var check = first.Value.Checks[0];
        Assert.Equal("not_connected", check.ConnectionStatus); Assert.Equal("accountant_confirmed", check.VerificationStatus);
        Assert.Equal("accountant_confirmed", check.LatestVerification!.Method);
        Assert.Equal("Accountant", check.LatestVerification.RecordedByName);
        Assert.Equal("pass", check.LatestVerification.Outcome);
        // Retry with an old token cannot append the same submission again.
        Assert.Equal(409, (await service.RecordManualAsync(BusinessId, Observation(setup.Version), Actor("accountant"), Ct)).StatusCode);
        var second = await service.RecordManualAsync(BusinessId, Observation(first.Value.Version) with { Outcome = "fail" }, Actor("accountant"), Ct);
        Assert.Equal("fail", second.Value!.Checks[0].LatestVerification!.Outcome);
        db.ChangeTracker.Clear();
        var history = (await service.HistoryAsync(BusinessId, Codes[0], 1, Actor("client", BusinessId), Ct)).Value!;
        Assert.Equal(2, history.Count); Assert.Contains(history, row => row.Outcome == "pass"); Assert.Contains(history, row => row.Outcome == "fail");
    }

    [Fact]
    public async Task DueManualObservationsBecomeStale_AndIdentifierChangesInvalidateThem()
    {
        await using var db = Database(); var service = new ComplianceMonitoringService(db);
        var setup = (await service.UpdateAsync(BusinessId, Setup(), Actor("admin"), Ct)).Value!;
        var observed = (await service.RecordManualAsync(BusinessId, Observation(setup.Version, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddDays(-1)), Actor("admin"), Ct)).Value!;
        Assert.Equal("stale", observed.Checks[0].VerificationStatus);
        var changed = (await service.UpdateAsync(BusinessId, Setup(observed.Version) with { RegistrationNumber = "NEW-IDENTIFIER" }, Actor("admin"), Ct)).Value!;
        Assert.Equal("identifiers_changed", changed.Checks[0].VerificationStatus);
        Assert.False(changed.Checks[0].LatestVerification!.MatchesCurrentIdentifiers);
        Assert.Equal("pass", changed.Checks[0].LatestVerification!.Outcome); // Historical fact retained, not silently rewritten.
    }

    [Fact]
    public async Task RejectsFutureCheckUnassessedCheckAndInventedAuthorityOutcome()
    {
        await using var db = Database(); var service = new ComplianceMonitoringService(db);
        var setup = (await service.UpdateAsync(BusinessId, Setup(), Actor("admin"), Ct)).Value!;
        Assert.NotNull((await service.RecordManualAsync(BusinessId, Observation(setup.Version) with { CheckedAtUtc = DateTime.UtcNow.AddDays(1) }, Actor("admin"), Ct)).Error);
        Assert.NotNull((await service.RecordManualAsync(BusinessId, Observation(setup.Version) with { CheckCode = "sars_tcs" }, Actor("admin"), Ct)).Error);
        Assert.NotNull((await service.RecordManualAsync(BusinessId, Observation(setup.Version) with { Outcome = "authority_verified" }, Actor("admin"), Ct)).Error);
        Assert.Empty(db.ComplianceVerifications);
    }

    [Fact]
    public async Task HistoryHasBoundedPaginationAndValidatesCheckCodes()
    {
        await using var db = Database(); var service = new ComplianceMonitoringService(db);
        Assert.NotNull((await service.HistoryAsync(BusinessId, "invented", 1, Actor("admin"), Ct)).Error);
        Assert.NotNull((await service.HistoryAsync(BusinessId, null, 0, Actor("admin"), Ct)).Error);
        Assert.Empty((await service.HistoryAsync(BusinessId, null, 1, Actor("admin"), Ct)).Value!);
    }
}
