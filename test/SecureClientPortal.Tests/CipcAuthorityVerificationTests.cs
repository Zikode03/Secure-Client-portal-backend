using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Compliance;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Tests;

public sealed class CipcAuthorityVerificationTests
{
    private static readonly Guid BusinessId = Guid.Parse("b2000000-0000-0000-0000-000000000010");
    private static readonly Guid StaffId = Guid.Parse("a2000000-0000-0000-0000-000000000010");
    private static readonly string[] Codes = ["cipc_registration", "cipc_annual_returns", "cipc_beneficial_ownership", "sars_tcs", "csd_registration"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeCipc : ICipcAuthorityClient
    {
        public bool IsConfigured(string checkCode) => checkCode.StartsWith("cipc_", StringComparison.Ordinal);
        public Task<AuthorityVerificationResult> VerifyAsync(string checkCode, string registrationNumber, CancellationToken ct)
        {
            var checkedAt = DateTime.UtcNow;
            return Task.FromResult(new AuthorityVerificationResult(true, "pass", "CIPC test reference", checkedAt, checkedAt.AddDays(30)));
        }
    }

    private static ClaimsPrincipal Accountant() => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, StaffId.ToString()),
        new Claim(ClaimTypes.Role, "accountant")
    ], "test"));

    private static PortalDbContext Database()
    {
        var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client = Client.Create(BusinessId, "CIPC Test Business", "Pty Ltd", "Contact", "contact@example.test", ClientStatus.Active);
        client.AssignAccountant(StaffId);
        db.Clients.Add(client);
        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), StaffId, BusinessId));
        var staff = User.CreateInvited(StaffId, "Accountant", "staff@example.test", UserRole.Accountant, "hash", "[]", null);
        staff.CompleteSetup("Accountant", "hash");
        db.Users.Add(staff);
        db.SaveChanges();
        return db;
    }

    private static UpdateComplianceMonitoringRequest Setup(Guid version = default) => new(
        version,
        "2026/123456/07",
        "1234567890",
        "MAAA0000000",
        Codes.Select(code => new CheckApplicabilityRequest(code, code.StartsWith("cipc_", StringComparison.Ordinal) ? "applies" : "undecided", "")).ToArray());

    [Fact]
    public async Task CipcConnector_IsOnlyPathThatCreatesAuthorityVerifiedRecord()
    {
        await using var db = Database();
        var service = new ComplianceMonitoringService(db, new FakeCipc());
        var setup = (await service.UpdateAsync(BusinessId, Setup(), Accountant(), Ct)).Value!;

        var result = await service.VerifyCipcAsync(BusinessId,
            new RunAuthorityVerificationRequest(setup.Version, "cipc_registration"), Accountant(), Ct);

        Assert.NotNull(result.Value);
        var check = result.Value.Checks.Single(x => x.Code == "cipc_registration");
        Assert.Equal("connected", check.ConnectionStatus);
        Assert.Equal("authority_verified", check.VerificationStatus);
        Assert.Equal("authority_verified", check.LatestVerification!.Method);
        Assert.Equal("pass", check.LatestVerification.Outcome);
        Assert.Equal("CIPC test reference", check.LatestVerification.EvidenceReference);

        var stored = await db.ComplianceVerifications.SingleAsync(Ct);
        Assert.Equal("authority_verified", stored.Method);
        Assert.Contains(await db.AuditLogs.ToListAsync(Ct), row => row.Action == "compliance.authority_verification_recorded");
    }

    [Fact]
    public async Task CipcFailure_DoesNotCreateAuthorityResult()
    {
        await using var db = Database();
        var failed = new FailedCipc();
        var service = new ComplianceMonitoringService(db, failed);
        var setup = (await service.UpdateAsync(BusinessId, Setup(), Accountant(), Ct)).Value!;

        var result = await service.VerifyCipcAsync(BusinessId,
            new RunAuthorityVerificationRequest(setup.Version, "cipc_registration"), Accountant(), Ct);

        Assert.NotNull(result.Error);
        Assert.Equal(503, result.StatusCode);
        Assert.Empty(db.ComplianceVerifications);
    }

    private sealed class FailedCipc : ICipcAuthorityClient
    {
        public bool IsConfigured(string checkCode) => true;
        public Task<AuthorityVerificationResult> VerifyAsync(string checkCode, string registrationNumber, CancellationToken ct) =>
            Task.FromResult(new AuthorityVerificationResult(false, "unknown", "", DateTime.UtcNow, DateTime.UtcNow, "CIPC unavailable"));
    }
}
