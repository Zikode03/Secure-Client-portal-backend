using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;
using SecureClientPortal.Backend.Models;
using System.Security.Claims;

namespace SecureClientPortal.Tests;

public class ComplianceAutomationTests
{
    [Fact]
    public async Task UnknownRegistration_DoesNotInventVatObligation()
    {
        await using var db = BuildDb();
        var client = BuildClient(Guid.NewGuid());
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var service = new ComplianceAutomationService(db);
        var admin = BuildAdmin();
        var run = await service.RunAsync(admin);
        var obligations = await service.GetObligationsAsync(admin, client.Id);

        Assert.Equal(0, run.Value!.ObligationsCreated);
        Assert.Empty(obligations.Value!);
        Assert.Contains(run.Value.Warnings, x => x.Contains("VAT201") && x.Contains("not confirmed"));
    }

    [Fact]
    public async Task VatObligation_IsCreatedOnceAcrossRepeatedAutomationRuns()
    {
        await using var db = BuildDb();
        var client = BuildClient(Guid.NewGuid());
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var service = new ComplianceAutomationService(db);
        var admin = BuildAdmin();
        await service.UpdateProfileAsync(
            client.Id,
            new UpdateClientComplianceProfileRequest(
                VatRegistered: true,
                PayeRegistered: false,
                UifRegistered: false,
                CoidaRegistered: false,
                ProvisionalTaxpayer: false,
                CompanyTaxRegistered: false,
                CipcRegistered: false),
            admin);

        var first = await service.RunAsync(admin, client.Id, new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc));
        var second = await service.RunAsync(admin, client.Id, new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc));
        var obligations = await service.GetObligationsAsync(admin, client.Id);

        Assert.Equal(1, first.Value!.ObligationsCreated);
        Assert.Equal(0, second.Value!.ObligationsCreated);
        Assert.Single(obligations.Value!);
        Assert.Equal("VAT201", obligations.Value![0].Code);
        Assert.Equal("waiting_for_client", obligations.Value[0].WorkflowStatus);
    }

    [Fact]
    public async Task ManualFiling_RequiresReview_AndPaymentKeepsObligationOpenUntilPaid()
    {
        await using var db = BuildDb();
        var client = BuildClient(Guid.NewGuid());
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var service = new ComplianceAutomationService(db);
        var admin = BuildAdmin();
        var effective = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await service.UpdateRulesAsync(
            new UpdateComplianceRuleSetRequest(
                "test-1",
                [new ComplianceRuleDefinitionDto(
                    "VAT201", "VAT201 return", "SARS", "TAX", 2, 1, null,
                    "VatRegistered", true, true, [], effective, null, "test-1")]),
            admin);
        await service.UpdateProfileAsync(
            client.Id,
            new UpdateClientComplianceProfileRequest(VatRegistered: true),
            admin);
        await service.RunAsync(admin, client.Id, new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc));
        var obligation = (await service.GetObligationsAsync(admin, client.Id)).Value!.Single();

        var premature = await service.RecordSubmissionAsync(
            obligation.Id,
            new RecordComplianceSubmissionRequest(DateTime.UtcNow, "REF-1", 1000m, null, true),
            admin);
        Assert.NotNull(premature.Error);
        Assert.Equal(409, premature.StatusCode);

        Assert.Null((await service.RecordPreparationAsync(obligation.Id, new RecordCompliancePreparationRequest(true), admin)).Error);
        Assert.Null((await service.RecordReviewAsync(obligation.Id, new RecordComplianceReviewRequest(true), admin)).Error);
        var submitted = await service.RecordSubmissionAsync(
            obligation.Id,
            new RecordComplianceSubmissionRequest(DateTime.UtcNow, "REF-1", 1000m, null, true),
            admin);
        Assert.Equal("payment_outstanding", submitted.Value!.WorkflowStatus);

        var paid = await service.RecordPaymentAsync(
            obligation.Id,
            new RecordCompliancePaymentRequest(DateTime.UtcNow, "PAY-1", 1000m),
            admin);
        Assert.Equal("complete", paid.Value!.WorkflowStatus);
    }

    [Fact]
    public async Task StarterRules_DoNotGuessStatutoryDueDays()
    {
        await using var db = BuildDb();
        var service = new ComplianceAutomationService(db);
        var rules = await service.GetRulesAsync(BuildAdmin());

        Assert.NotEmpty(rules.Value!.Rules);
        Assert.All(rules.Value.Rules, rule => Assert.Null(rule.DueDayOfMonth));
    }

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"compliance-automation-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static Client BuildClient(Guid id) => Client.Create(
        id,
        "Automation Test Client",
        "Private Company",
        "Finance Contact",
        $"finance-{id:N}@example.test",
        ClientStatus.Active);

    private static ClaimsPrincipal BuildAdmin()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "admin")
            ],
            "test");
        return new ClaimsPrincipal(identity);
    }
}
