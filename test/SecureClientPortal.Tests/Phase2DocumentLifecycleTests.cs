using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Controllers;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Documents;
using SecureClientPortal.Backend.Infrastructure.Requests.Application;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class Phase2DocumentLifecycleTests
{
    private static readonly Guid AccountantUserId = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("c3333333-3333-3333-3333-333333333333");
    private static readonly Guid ClientAlphaId = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1");
    private static readonly Guid MonthlyPackId = Guid.Parse("c4444444-4444-4444-4444-444444444444");
    private static readonly Guid SlotId = Guid.Parse("c5555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task ClientUpload_Reject_Reupload_KeepsVersionsAndAuditTrail()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new InMemoryFileStorage();

        var clientController = BuildDocumentsController(db, storage, BuildUser(ClientUserId, "client", [ClientAlphaId]));

        var uploadOne = await clientController.Upload(new UploadDocumentRequest
        {
            ClientId = ClientAlphaId,
            MonthlyPackId = MonthlyPackId,
            DocumentSlotId = SlotId,
            DocumentType = "bank_statement",
            File = BuildFormFile("bank-statement-v1.pdf", "First bank statement")
        }, TestContext.Current.CancellationToken);

        Assert.IsType<CreatedResult>(uploadOne);

        var document = await db.Documents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("uploaded", document.Status);
        Assert.Equal(1, document.CurrentVersionNumber);

        var packAfterUpload = await db.MonthlyPacks.SingleAsync(TestContext.Current.CancellationToken);
        var slotAfterUpload = await db.DocumentSlots.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("in_progress", packAfterUpload.Status);
        Assert.Equal("uploaded", slotAfterUpload.Status);

        var accountantController = BuildDocumentsController(db, storage, BuildUser(AccountantUserId, "accountant"));

        var getResult = await accountantController.GetById(document.Id.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(getResult);

        var rejectResult = await accountantController.Review(
            document.Id.ToString(),
            new AddReviewDecisionRequest("rejected", "The closing balance is cut off.", null),
            TestContext.Current.CancellationToken);

        var rejectOk = Assert.IsType<OkObjectResult>(rejectResult);
        var rejectJson = JsonSerializer.Serialize(rejectOk.Value);
        Assert.Contains("\"documentStatus\":\"rejected\"", rejectJson);

        var rejectedDocument = await db.Documents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("rejected", rejectedDocument.Status);
        var reuploadRequest = await db.Requests.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("reupload_required", reuploadRequest.RequestType);
        Assert.Equal("waiting_on_client", reuploadRequest.Status);
        Assert.Equal(document.Id, reuploadRequest.RelatedDocumentId);

        var clientNotifications = await db.Notifications.Where(x => x.UserId == ClientUserId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(clientNotifications, x => x.Type == "document.rejected");

        var uploadTwo = await clientController.Upload(new UploadDocumentRequest
        {
            ClientId = ClientAlphaId,
            MonthlyPackId = MonthlyPackId,
            DocumentSlotId = SlotId,
            DocumentType = "bank_statement",
            DocumentId = document.Id,
            File = BuildFormFile("bank-statement-v2.pdf", "Corrected bank statement")
        }, TestContext.Current.CancellationToken);

        Assert.IsType<CreatedResult>(uploadTwo);

        var finalDocument = await db.Documents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("uploaded", finalDocument.Status);
        Assert.Equal(2, finalDocument.CurrentVersionNumber);

        var requestController = new RequestsController(new RequestService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientAlphaId]))
        };
        var commentResult = await requestController.AddComment(
            reuploadRequest.Id.ToString(),
            new AddRequestCommentRequest("Corrected version uploaded."),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(commentResult.Result);

        reuploadRequest = await db.Requests.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("waiting_on_accountant", reuploadRequest.Status);

        var versionsResult = await clientController.GetVersions(document.Id.ToString(), TestContext.Current.CancellationToken);
        var versionsOk = Assert.IsType<OkObjectResult>(versionsResult);
        var versionsJson = JsonSerializer.Serialize(versionsOk.Value);
        Assert.Contains("\"VersionNumber\":2", versionsJson);
        Assert.Contains("\"VersionNumber\":1", versionsJson);
        Assert.Contains("\"isCurrent\":true", versionsJson);

        var finalPack = await db.MonthlyPacks.SingleAsync(TestContext.Current.CancellationToken);
        var finalSlot = await db.DocumentSlots.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("reopened", finalPack.Status);
        Assert.Equal("uploaded", finalSlot.Status);

        var accountantRequestController = new RequestsController(new RequestService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantUserId, "accountant"))
        };
        var resolveResult = await accountantRequestController.Resolve(
            reuploadRequest.Id.ToString(),
            new ResolveRequestRequest("Corrected document received."),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(resolveResult.Result);

        reuploadRequest = await db.Requests.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("resolved", reuploadRequest.Status);

        var auditActions = await db.AuditLogs
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Action)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains("documents.uploaded", auditActions);
        Assert.Contains("documents.rejected", auditActions);
        Assert.Contains("request.created", auditActions);
        Assert.Contains("comment.added", auditActions);
        Assert.Contains("request.resolved", auditActions);
        Assert.Contains("notification.sent", auditActions);
    }

    [Fact]
    public async Task RequestReupload_RecordsAuditAndKeepsDownloadAvailable()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new InMemoryFileStorage();

        var clientController = BuildDocumentsController(db, storage, BuildUser(ClientUserId, "client", [ClientAlphaId]));

        var upload = await clientController.Upload(new UploadDocumentRequest
        {
            ClientId = ClientAlphaId,
            MonthlyPackId = MonthlyPackId,
            DocumentSlotId = SlotId,
            DocumentType = "bank_statement",
            File = BuildFormFile("bank-statement.pdf", "Download me")
        }, TestContext.Current.CancellationToken);
        Assert.IsType<CreatedResult>(upload);

        var document = await db.Documents.SingleAsync(TestContext.Current.CancellationToken);

        var accountantController = BuildDocumentsController(db, storage, BuildUser(AccountantUserId, "accountant"));

        var requestReupload = await accountantController.RequestReupload(
            document.Id.ToString(),
            new RequestReuploadRequest("Please upload the full statement with all pages.", null),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(requestReupload);

        var auditActions = await db.AuditLogs.Select(x => x.Action).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("documents.reupload_requested", auditActions);
        Assert.Contains("request.created", auditActions);

        var download = await clientController.Download(document.Id.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<FileStreamResult>(download);

        var accessLogs = await db.DocumentAccessLogs
            .Where(x => x.DocumentId == document.Id)
            .OrderBy(x => x.AccessedAtUtc)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(accessLogs, x => x.Action == "download" && x.AccessedByRole == "client");
    }

    [Fact]
    public async Task ViewAndDownload_WriteDocumentAccessLogs()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new InMemoryFileStorage();

        var uploadController = BuildDocumentsController(db, storage, BuildUser(ClientUserId, "client", [ClientAlphaId]));

        var upload = await uploadController.Upload(new UploadDocumentRequest
        {
            ClientId = ClientAlphaId,
            MonthlyPackId = MonthlyPackId,
            DocumentSlotId = SlotId,
            DocumentType = "bank_statement",
            File = BuildFormFile("bank-statement.pdf", "Audit me")
        }, TestContext.Current.CancellationToken);
        Assert.IsType<CreatedResult>(upload);

        var document = await db.Documents.SingleAsync(TestContext.Current.CancellationToken);

        var readerController = BuildDocumentsController(db, storage, BuildUser(AccountantUserId, "accountant"));

        var getResult = await readerController.GetById(document.Id.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(getResult);

        var downloadResult = await readerController.Download(document.Id.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<FileStreamResult>(downloadResult);

        var accessLogs = await db.DocumentAccessLogs
            .Where(x => x.DocumentId == document.Id)
            .OrderBy(x => x.AccessedAtUtc)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(accessLogs, x => x.Action == "view" && x.AccessedByRole == "accountant");
        Assert.Contains(accessLogs, x => x.Action == "download" && x.AccessedByRole == "accountant");
    }

    private static DocumentsController BuildDocumentsController(PortalDbContext db, IFileStorage storage, ClaimsPrincipal user)
    {
        return new DocumentsController(new DocumentWorkflowService(db, storage))
        {
            ControllerContext = BuildControllerContext(user)
        };
    }

    private static FormFile BuildFormFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private static ControllerContext BuildControllerContext(ClaimsPrincipal user)
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    private static ClaimsPrincipal BuildUser(Guid userId, string role, IEnumerable<Guid>? clientIds = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };

        if (clientIds is not null)
        {
            claims.AddRange(clientIds.Select(x => new Claim("client_id", x.ToString())));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"phase2-doc-lifecycle-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            BuildActiveUser(AccountantUserId, "Accountant", "acc@test.com", UserRole.Accountant),
            BuildActiveUser(ClientUserId, "Client", "client@test.com", UserRole.Client, [ClientAlphaId]));

        var client = Client.Create(ClientAlphaId, "Alpha", "Pty Ltd", "A", "a@test.com", ClientStatus.Active);
        client.AssignAccountant(AccountantUserId);
        client.UpdateComplianceHealth(90);
        db.Clients.Add(client);

        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), AccountantUserId, ClientAlphaId));

        db.MonthlyPacks.Add(MonthlyPack.Create(MonthlyPackId, ClientAlphaId, 2026, 6));

        var slot = DocumentSlot.Create(SlotId, MonthlyPackId, ClientAlphaId, "bank_statement", "Bank Statement", true, null);
        db.DocumentSlots.Add(slot);

        db.SaveChanges();
    }

    private static User BuildActiveUser(Guid id, string fullName, string email, UserRole role, IEnumerable<Guid>? clientIds = null)
    {
        var user = User.CreateInvited(
            id,
            fullName,
            email,
            role,
            "x",
            JsonSerializer.Serialize(clientIds?.Select(x => x.ToString()).ToArray() ?? Array.Empty<string>()),
            null);
        user.CompleteSetup(fullName, "x");
        return user;
    }

    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public async Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            var key = $"{clientId}/{Guid.NewGuid():N}-{file.FileName}";
            _files[key] = stream.ToArray();
            return new StoredFile(key, file.FileName, file.FileName, file.ContentType, file.Length);
        }

        public Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default)
        {
            if (!_files.TryGetValue(storageKey, out var bytes))
            {
                return Task.FromResult<StoredFileContent?>(null);
            }

            return Task.FromResult<StoredFileContent?>(new StoredFileContent(new MemoryStream(bytes), "application/pdf"));
        }
    }
}



