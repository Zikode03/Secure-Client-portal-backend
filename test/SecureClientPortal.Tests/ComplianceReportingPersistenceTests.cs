using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Reports;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class ComplianceReportingPersistenceTests
{
    private static readonly Guid AccountantId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid ClientUserId = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherClientUserId = Guid.Parse("c2000000-0000-0000-0000-000000000001");
    private static readonly Guid ClientId = Guid.Parse("b1000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherClientId = Guid.Parse("b2000000-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("d1000000-0000-0000-0000-000000000001");
    private static readonly Guid ItemId = Guid.Parse("e1000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task EvidenceUpload_PersistsVersionsHistoryAndScopedDownloads()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new TestFileStorage();
        var service = new ComplianceService(db, storage, RequestService.CreateForTests(db));
        var clientActor = BuildUser(ClientUserId, "client", [ClientId]);

        var first = await service.UploadEvidenceAsync(
            ItemId.ToString(),
            new UploadComplianceEvidenceRequest
            {
                File = BuildFormFile("tax-pin-v1.pdf", "version-one"),
                Note = "Initial evidence"
            },
            clientActor,
            TestContext.Current.CancellationToken);
        Assert.NotNull(first.Value);
        Assert.Equal(1, first.Value.VersionNumber);

        var second = await service.UploadEvidenceAsync(
            ItemId.ToString(),
            new UploadComplianceEvidenceRequest
            {
                File = BuildFormFile("tax-pin-v2.pdf", "version-two"),
                Note = "Renewed evidence"
            },
            clientActor,
            TestContext.Current.CancellationToken);
        Assert.NotNull(second.Value);
        Assert.Equal(2, second.Value.VersionNumber);

        var versionsResult = await service.GetEvidenceVersionsAsync(ItemId.ToString(), clientActor, TestContext.Current.CancellationToken);
        var versions = Assert.IsAssignableFrom<IReadOnlyList<ComplianceEvidenceVersionResponse>>(versionsResult.Value);
        Assert.Equal(2, versions.Count);
        Assert.True(versions[0].IsCurrentVersion);
        Assert.False(versions[1].IsCurrentVersion);
        Assert.Equal("pending", (await db.ComplianceItems.SingleAsync(TestContext.Current.CancellationToken)).Status);

        var historyResult = await service.GetHistoryAsync(clientActor, ClientId.ToString(), ItemId.ToString(), ct: TestContext.Current.CancellationToken);
        var history = Assert.IsAssignableFrom<IReadOnlyList<ComplianceHistoryEntryResponse>>(historyResult.Value);
        Assert.Equal(2, history.Count(x => x.Action == "compliance.evidence_uploaded"));

        var download = await service.DownloadEvidenceAsync(second.Value.Id.ToString(), clientActor, TestContext.Current.CancellationToken);
        Assert.NotNull(download.Value.Content);
        using var reader = new StreamReader(download.Value.Content.Stream, Encoding.UTF8);
        Assert.Equal("version-two", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));

        var forbidden = await service.GetEvidenceVersionsAsync(
            ItemId.ToString(),
            BuildUser(OtherClientUserId, "client", [OtherClientId]),
            TestContext.Current.CancellationToken);
        Assert.True(forbidden.Forbidden);
    }

    [Fact]
    public async Task ComplianceRequest_PersistsRequestAndLinksAuditHistory()
    {
        await using var db = BuildDb();
        Seed(db);
        var service = new ComplianceService(db, new TestFileStorage(), RequestService.CreateForTests(db));
        var accountant = BuildUser(AccountantId, "accountant");

        var result = await service.CreateWorkflowRequestAsync(
            ItemId.ToString(),
            new CreateComplianceWorkflowRequest("renewal_request", DateTime.UtcNow.AddDays(7), "Please upload a renewed tax PIN."),
            accountant,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.Value);
        Assert.Equal("compliance_renewal", result.Value.RequestType);
        Assert.Equal("waiting_on_client", result.Value.Status);
        Assert.Equal(ItemId, (await db.AuditLogs.SingleAsync(x => x.Action == "compliance.request_created", TestContext.Current.CancellationToken)).EntityId);
    }

    [Fact]
    public async Task CompliancePdfAndSchedules_AreRealScopedAndPersisted()
    {
        await using var db = BuildDb();
        Seed(db);
        var service = new ReportService(db);
        var clientActor = BuildUser(ClientUserId, "client", [ClientId]);

        var pdfResult = await service.GenerateCompliancePdfAsync(clientActor, ClientId.ToString(), TestContext.Current.CancellationToken);
        var pdf = Assert.IsType<ReportFileResponse>(pdfResult.Value);
        Assert.True(pdf.Content.Length > 500);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf.Content, 0, 4));
        Assert.EndsWith(".pdf", pdf.FileName);
        Assert.Contains(await db.AuditLogs.ToListAsync(TestContext.Current.CancellationToken), x => x.Action == "compliance.report_downloaded");

        var createResult = await service.CreateScheduleAsync(
            new CreateReportScheduleRequest(null, "weekly", ["Finance@Example.test", "finance@example.test"]),
            clientActor,
            TestContext.Current.CancellationToken);
        var schedule = Assert.IsType<ReportScheduleResponse>(createResult.Value);
        Assert.Equal(ClientId, schedule.ClientId);
        Assert.Equal("weekly", schedule.Frequency);
        Assert.Single(schedule.Recipients);
        Assert.Equal("finance@example.test", schedule.Recipients.Single());
        Assert.True(schedule.NextRunAtUtc > DateTime.UtcNow);

        var listed = await service.GetSchedulesAsync(clientActor, ClientId.ToString(), TestContext.Current.CancellationToken);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ReportScheduleResponse>>(listed.Value));

        var forbiddenUpdate = await service.UpdateScheduleAsync(
            schedule.Id.ToString(),
            new UpdateReportScheduleRequest("monthly", ["other@example.test"]),
            BuildUser(OtherClientUserId, "client", [OtherClientId]),
            TestContext.Current.CancellationToken);
        Assert.True(forbiddenUpdate.Forbidden);

        var delete = await service.DeleteScheduleAsync(schedule.Id.ToString(), clientActor, TestContext.Current.CancellationToken);
        Assert.True(delete.Value);
        Assert.Empty(await db.ReportSchedules.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"compliance-reporting-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            BuildActiveUser(AccountantId, "Accountant", "accountant@example.test", UserRole.Accountant),
            BuildActiveUser(ClientUserId, "Client User", "client@example.test", UserRole.Client, [ClientId]),
            BuildActiveUser(OtherClientUserId, "Other Client", "other@example.test", UserRole.Client, [OtherClientId]));

        var client = Client.Create(ClientId, "Apex Trading", "Pty Ltd", "Client User", "client@example.test", ClientStatus.Active);
        client.AssignAccountant(AccountantId);
        client.UpdateComplianceHealth(70);
        var otherClient = Client.Create(OtherClientId, "Other Trading", "Pty Ltd", "Other Client", "other@example.test", ClientStatus.Active);
        otherClient.AssignAccountant(AccountantId);
        db.Clients.AddRange(client, otherClient);
        db.ClientAssignments.AddRange(
            ClientAssignment.Create(Guid.NewGuid(), AccountantId, ClientId),
            ClientAssignment.Create(Guid.NewGuid(), AccountantId, OtherClientId));

        db.ComplianceCategories.Add(ComplianceCategory.Create(CategoryId, "Tax Compliance", "Tax records", "TAX", true));
        db.ComplianceItems.Add(ComplianceItem.Create(
            ItemId,
            ClientId,
            CategoryId,
            "Tax compliance PIN",
            ComplianceItemStatus.Missing,
            AccountantId,
            ComplianceRiskLevel.High,
            "compliance_record",
            DateTime.UtcNow.AddDays(7),
            DateTime.UtcNow.AddDays(30)));
        db.SaveChanges();
    }

    private static User BuildActiveUser(Guid id, string name, string email, UserRole role, IEnumerable<Guid>? clientIds = null)
    {
        var user = User.CreateInvited(
            id,
            name,
            email,
            role,
            "hash",
            JsonSerializer.Serialize(clientIds?.Select(x => x.ToString()).ToArray() ?? []),
            null);
        user.CompleteSetup(name, "hash");
        return user;
    }

    private static ClaimsPrincipal BuildUser(Guid id, string role, IEnumerable<Guid>? clientIds = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, id.ToString()),
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Name, role == "accountant" ? "Accountant" : "Client User"),
            new(ClaimTypes.Role, role)
        };
        if (clientIds is not null)
        {
            claims.AddRange(clientIds.Select(x => new Claim("client_id", x.ToString())));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static IFormFile BuildFormFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private sealed class TestFileStorage : IFileStorage
    {
        private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _files = [];

        public async Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            var key = $"{clientId}/{Guid.NewGuid():N}/{file.FileName}";
            _files[key] = (stream.ToArray(), file.ContentType);
            return new StoredFile(key, file.FileName, file.FileName, file.ContentType, file.Length);
        }

        public Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default)
        {
            if (!_files.TryGetValue(storageKey, out var file))
            {
                return Task.FromResult<StoredFileContent?>(null);
            }

            return Task.FromResult<StoredFileContent?>(new StoredFileContent(new MemoryStream(file.Bytes), file.ContentType));
        }
    }
}
