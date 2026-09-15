using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Api.Modules.Compliance;
using SecureClientPortal.Backend.Application.Contracts.Modules.Compliance;
using SecureClientPortal.Backend.Application.Modules.Documents;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Tests;

public sealed class ComplianceAutomationApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static ClaimsPrincipal Actor(string role, Guid? clientId = null) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, ActorId.ToString()), new Claim(ClaimTypes.Role, role),
        new Claim("client_id", (clientId ?? ClientId).ToString())
    ], "test"));
    private static PortalDbContext Database()
    {
        var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client = Client.Create(ClientId, "Business", "Pty Ltd", "Contact", "client@example.test", ClientStatus.Active);
        client.AssignAccountant(ActorId);
        db.Clients.Add(client);
        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), ActorId, ClientId));
        db.SaveChanges();
        return db;
    }
    private static ComplianceAutomationService Service(PortalDbContext db, IFileStorage? storage = null) =>
        new(db, new ComplianceService(db, storage));
    private static async Task<ComplianceObligationResponse> Generate(ComplianceAutomationService service, bool csd = true)
    {
        var profile = await service.UpdateProfileAsync(ClientId, new ClientComplianceProfile { GovernmentSupplier = csd, PayeRegistered = !csd }, Actor("admin"), Ct);
        Assert.Null(profile.Error);
        var run = await service.RunAsync(ClientId, Actor("admin"), Ct);
        Assert.Null(run.Error);
        return Assert.Single((await service.GetObligationsAsync(ClientId, Actor("admin"), Ct)).Value!.Where(x => x.Code == (csd ? "CSD" : "EMP201")));
    }
    private static FormFile File()
    {
        var bytes = Encoding.UTF8.GetBytes("%PDF-test evidence");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", "receipt.pdf")
        { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    }

    [Fact]
    public void Controller_CoversFrontendRoutes_AndRestrictsStaffWrites()
    {
        var methods = typeof(ComplianceAutomationController).GetMethods();
        var routes = methods.SelectMany(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), true).Cast<HttpMethodAttribute>())
            .Select(x => x.Template).ToArray();
        foreach (var route in new[] { "rules", "profiles/{clientId:guid}", "obligations", "run",
            "obligations/{id:guid}/preparation", "obligations/{id:guid}/review", "obligations/{id:guid}/submission",
            "obligations/{id:guid}/payment", "obligations/{id:guid}/not-applicable", "obligations/{id:guid}/evidence" })
            Assert.Contains(route, routes);
        foreach (var name in new[] { "Run", "Preparation", "Review", "Submission", "Payment", "NotApplicable" })
            Assert.Contains(methods.Single(m => m.Name == name).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
                a => a.Policy == "AccountantOnly");
    }

    [Fact]
    public async Task UnknownProfile_DoesNotInventObligations()
    {
        await using var db = Database();
        var service = Service(db);
        var profile = (await service.GetProfileAsync(ClientId, Actor("client"), Ct)).Value!;
        Assert.Null(profile.VatRegistered);
        Assert.Null(profile.UpdatedAtUtc);
        Assert.Equal(0, (await service.RunAsync(null, Actor("admin"), Ct)).Value!.ObligationsCreated);
    }

    [Fact]
    public async Task RunsAreIdempotent_AndEvidenceIdentityIsSeparate()
    {
        await using var db = Database();
        var service = Service(db);
        var first = await Generate(service);
        var second = await service.RunAsync(ClientId, Actor("accountant"), Ct);
        Assert.Equal(0, second.Value!.ObligationsCreated);
        Assert.Single(await db.ComplianceObligations.ToListAsync(Ct));
        var row = await db.ComplianceObligations.SingleAsync(Ct);
        Assert.NotEqual(row.Id, row.ComplianceItemId);
        Assert.Equal(row.ClientId, (await db.ComplianceItems.SingleAsync(Ct)).ClientId);
        Assert.Null(first.DueDateUtc);
        Assert.Equal("not_required", first.SubmissionStatus);
        Assert.Equal("waiting_for_client", first.Readiness);
    }

    [Fact]
    public async Task Upload_UsesLinkedItem_IsReadable_AndDoesNotAutoApprove()
    {
        await using var db = Database();
        var storage = new MemoryStorage();
        var service = Service(db, storage);
        var item = await Generate(service);
        var result = await service.UploadEvidenceAsync(item.Id, new() { File = File(), Note = "CSD report" }, Actor("client"), Ct);
        Assert.Null(result.Error);
        var response = result.Value!;
        var row = await db.ComplianceObligations.SingleAsync(Ct);
        Assert.Equal(row.ComplianceItemId, response.Evidence.ComplianceItemId);
        Assert.NotEqual(item.Id, response.Evidence.ComplianceItemId);
        Assert.Equal(1, response.Obligation.EvidenceFound);
        Assert.Equal("ready_to_prepare", response.Obligation.Readiness);
        Assert.Equal("not_started", response.Obligation.ReviewStatus);
        Assert.Single((await service.GetEvidenceAsync(item.Id, Actor("client"), Ct)).Value!);
        var download = await new ComplianceService(db, storage).DownloadEvidenceAsync(response.Evidence.Id.ToString(), Actor("client"), Ct);
        Assert.Null(download.Error);
        var prepared = await service.PreparationAsync(item.Id, new(true, null), Actor("accountant"), Ct);
        Assert.Equal("ready_for_review", prepared.Value!.Readiness);
        var reviewed = await service.ReviewAsync(item.Id, new(true, null), Actor("accountant"), Ct);
        Assert.Equal("complete", reviewed.Value!.Readiness);
        Assert.Contains(await db.AuditLogs.ToListAsync(Ct), x => x.Action == "compliance.automation.review");
        var replacement = await service.UploadEvidenceAsync(item.Id, new() { File = File() }, Actor("client"), Ct);
        Assert.Equal("ready_to_prepare", replacement.Value!.Obligation.Readiness);
        Assert.Equal("not_started", replacement.Value.Obligation.ReviewStatus);
        Assert.Equal(2, (await service.GetEvidenceAsync(item.Id, Actor("client"), Ct)).Value!.Count);
    }

    [Fact]
    public async Task FailedStorage_CannotCreateEvidenceOrReadiness()
    {
        await using var db = Database();
        var service = Service(db, new MemoryStorage { Fail = true });
        var item = await Generate(service);
        await Assert.ThrowsAsync<IOException>(() => service.UploadEvidenceAsync(item.Id, new() { File = File() }, Actor("client"), Ct));
        Assert.Empty(await db.ComplianceEvidenceVersions.ToListAsync(Ct));
        Assert.Equal(0, (await service.GetObligationsAsync(ClientId, Actor("client"), Ct)).Value![0].EvidenceFound);
    }

    [Fact]
    public async Task ForeignClientCannotReadOrUpload_AndClientCannotPerformStaffActions()
    {
        await using var db = Database();
        var service = Service(db, new MemoryStorage());
        var item = await Generate(service);
        var foreign = Actor("client", Guid.NewGuid());
        Assert.True((await service.GetProfileAsync(ClientId, foreign, Ct)).Forbidden);
        Assert.True((await service.GetObligationsAsync(ClientId, foreign, Ct)).Forbidden);
        Assert.Empty((await service.GetObligationsAsync(null, foreign, Ct)).Value!);
        Assert.True((await service.UploadEvidenceAsync(item.Id, new() { File = File() }, foreign, Ct)).Forbidden);
        Assert.True((await service.GetEvidenceAsync(item.Id, foreign, Ct)).Forbidden);
        Assert.True((await service.RunAsync(null, Actor("client"), Ct)).Forbidden);
        Assert.True((await service.PreparationAsync(item.Id, new(true, null), Actor("client"), Ct)).Forbidden);
        Assert.True((await service.UpdateProfileAsync(ClientId, new ClientComplianceProfile(), Actor("client"), Ct)).Forbidden);
        Assert.Empty(await db.ComplianceEvidenceVersions.ToListAsync(Ct));
    }

    [Fact]
    public async Task EvidenceForAnotherObligationCannotSatisfyMonthlyCategories()
    {
        await using var db = Database();
        var service = Service(db, new MemoryStorage());
        var item = await Generate(service, csd: false);
        var result = await service.UploadEvidenceAsync(item.Id, new() { File = File() }, Actor("accountant"), Ct);
        Assert.Equal(0, result.Value!.Obligation.EvidenceFound);
        Assert.NotNull((await service.PreparationAsync(item.Id, new(true, null), Actor("accountant"), Ct)).Error);
        Assert.NotNull((await service.ReviewAsync(item.Id, new(true, null), Actor("accountant"), Ct)).Error);
    }

    [Fact]
    public async Task FilingAndPayment_RequireReviewReferencesAndFullPayment()
    {
        await using var db = Database();
        var service = Service(db);
        var rules = (await service.GetRulesAsync(Actor("admin"), Ct)).Value!;
        var rule = rules.Rules.Single(r => r.Code == "EMP201") with { RequiredDocumentCategories = [] };
        Assert.Null((await service.UpdateRulesAsync(new UpdateComplianceRulesRequest("verified-test", [rule]), Actor("admin"), Ct)).Error);
        var item = await Generate(service, csd: false);
        var submission = new SubmissionRequest(DateTime.UtcNow, "SARS-123", 100m, null, true, null);
        Assert.NotNull((await service.SubmissionAsync(item.Id, submission, Actor("accountant"), Ct)).Error);
        await service.PreparationAsync(item.Id, new(true, null), Actor("accountant"), Ct);
        await service.ReviewAsync(item.Id, new(true, null), Actor("accountant"), Ct);
        Assert.NotNull((await service.SubmissionAsync(item.Id, submission with { PaymentRequired = false }, Actor("accountant"), Ct)).Error);
        Assert.Equal("payment_outstanding", (await service.SubmissionAsync(item.Id, submission, Actor("accountant"), Ct)).Value!.Readiness);
        Assert.NotNull((await service.PaymentAsync(item.Id, new(DateTime.UtcNow, "payment", 20, null), Actor("accountant"), Ct)).Error);
        var paid = await service.PaymentAsync(item.Id, new(DateTime.UtcNow, "payment", 100, null), Actor("accountant"), Ct);
        Assert.Equal("complete", paid.Value!.Readiness);
        Assert.Equal(100m, paid.Value.AmountPaid);
        Assert.NotNull((await service.SubmissionAsync(item.Id, submission, Actor("accountant"), Ct)).Error);
    }

    [Fact]
    public async Task RuleVersionsAreImmutable_AndInvalidConfigurationIsRejected()
    {
        await using var db = Database();
        var service = Service(db);
        var item = await Generate(service);
        var rules = (await service.GetRulesAsync(Actor("admin"), Ct)).Value!;
        Assert.True((await service.UpdateRulesAsync(new UpdateComplianceRulesRequest("v2", rules.Rules), Actor("accountant"), Ct)).Forbidden);
        Assert.NotNull((await service.UpdateRulesAsync(new UpdateComplianceRulesRequest("v2", [rules.Rules[0] with { CadenceMonths = 0 }]), Actor("admin"), Ct)).Error);
        Assert.Null((await service.UpdateRulesAsync(new UpdateComplianceRulesRequest("v2", rules.Rules), Actor("admin"), Ct)).Error);
        Assert.Equal(409, (await service.UpdateRulesAsync(new UpdateComplianceRulesRequest("v2", rules.Rules), Actor("admin"), Ct)).StatusCode);
        await service.RunAsync(ClientId, Actor("admin"), Ct);
        Assert.Equal(item.RuleVersion, (await service.GetObligationsAsync(ClientId, Actor("admin"), Ct)).Value![0].RuleVersion);
        Assert.NotNull((await service.UpdateProfileAsync(ClientId, new ClientComplianceProfile() { VatCycleMonths = 0 }, Actor("admin"), Ct)).Error);
    }

    [Fact]
    public async Task EvidenceLinkCorruptionFailsClosed()
    {
        await using var db = Database();
        var service = Service(db, new MemoryStorage());
        var item = await Generate(service);
        (await db.ComplianceObligations.SingleAsync(Ct)).ComplianceItemId = Guid.NewGuid();
        await db.SaveChangesAsync(Ct);
        Assert.Equal(409, (await service.UploadEvidenceAsync(item.Id, new() { File = File() }, Actor("client"), Ct)).StatusCode);
        Assert.Empty(await db.ComplianceEvidenceVersions.ToListAsync(Ct));
    }


    [Fact]
    public async Task LegacyRecordsKeepTheirIdentityFilingAndEvidenceLinks()
    {
        await using var db = Database();
        var category = ComplianceCategory.Create(Guid.NewGuid(), "Legacy CSD", "Supplier evidence", "CSD");
        var item = ComplianceItem.Create(Guid.NewGuid(), ClientId, category.Id, "Legacy registration",
            ComplianceItemStatus.Pending, ActorId, ComplianceRiskLevel.Medium, null, null, null);
        db.ComplianceCategories.Add(category);
        db.ComplianceItems.Add(item);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var state = new ComplianceObligationResponse { Id = item.Id, ClientId = ClientId, Code = "CSD",
            Name = "Legacy registration", Authority = "National Treasury", RuleVersion = "legacy-v1",
            PeriodStartUtc = new DateTime(2026, 1, 1), PeriodEndUtc = new DateTime(2026, 12, 31),
            PreparationStatus = "complete", ReviewStatus = "approved", SubmissionStatus = "not_required" };
        var legacyKey = "compliance.automation.obligation:" + item.Id.ToString("N");
        db.SystemSettings.Add(SystemSetting.Create(legacyKey, JsonSerializer.Serialize(state, json)));
        db.SystemSettings.Add(SystemSetting.Create("compliance.automation.profile:" + ClientId.ToString("N"),
            JsonSerializer.Serialize(new ClientComplianceProfile { ClientId = ClientId, GovernmentSupplier = true,
                VatRegistered = true, VatCycleMonths = 2, VatAnchorMonth = 1, UpdatedAtUtc = DateTime.UtcNow }, json)));
        await db.SaveChangesAsync(Ct);
        var service = Service(db, new MemoryStorage());
        var profile = (await service.GetProfileAsync(ClientId, Actor("client"), Ct)).Value!;
        Assert.Equal(2, profile.VatAnchorMonth);
        var imported = Assert.Single((await service.GetObligationsAsync(ClientId, Actor("client"), Ct)).Value!);
        Assert.Equal(item.Id, imported.Id);
        Assert.Equal("legacy-v1", imported.RuleVersion);
        Assert.Equal("approved", imported.ReviewStatus);
        var upload = await service.UploadEvidenceAsync(imported.Id, new() { File = File() }, Actor("client"), Ct);
        Assert.Equal(item.Id, upload.Value!.Evidence.ComplianceItemId);
        Assert.Equal("not_started", upload.Value.Obligation.ReviewStatus);
        await service.GetObligationsAsync(ClientId, Actor("client"), Ct);
        Assert.Single(await db.ComplianceObligations.ToListAsync(Ct));
        Assert.True(await db.SystemSettings.AnyAsync(x => x.Key == legacyKey, Ct));
        await service.RunSystemAsync(new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc), Ct);
        Assert.Single(await db.ComplianceObligations.Where(x => x.Code == "CSD").ToListAsync(Ct));
    }

    [Fact]
    public async Task SchedulerCreatesMissingEvidenceRequestsOnce()
    {
        await using var db = Database();
        var service = Service(db);
        await service.UpdateProfileAsync(ClientId, new ClientComplianceProfile { GovernmentSupplier = true }, Actor("admin"), Ct);
        var now = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var first = await service.RunSystemAsync(now, Ct);
        var second = await service.RunSystemAsync(now, Ct);
        Assert.Equal(1, first.MissingEvidenceRequestsCreated);
        Assert.Equal(0, second.MissingEvidenceRequestsCreated);
        Assert.Single(await db.Requests.ToListAsync(Ct));
        Assert.Equal(now, first.RunAtUtc);
    }

    private sealed class MemoryStorage : IFileStorage
    {
        public bool Fail { get; init; }
        public Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
        {
            if (Fail) throw new IOException("Scanner/storage unavailable");
            return Task.FromResult(new StoredFile(clientId + "/receipt", file.FileName, file.FileName, file.ContentType, file.Length));
        }
        public Task<StoredFileContent?> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<StoredFileContent?>(new(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-test evidence")), "application/pdf"));
    }
}
