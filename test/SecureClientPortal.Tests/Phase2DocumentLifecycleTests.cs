using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Controllers;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class Phase2DocumentLifecycleTests
{
    [Fact]
    public async Task ClientUpload_Reject_Reupload_KeepsVersionsAndAuditTrail()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new InMemoryFileStorage();

        var clientController = new DocumentsController(db, storage)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };

        var uploadOne = await clientController.Upload(new UploadDocumentRequest
        {
            ClientId = "c_001",
            MonthlyPackId = "mp_001",
            DocumentSlotId = "slot_001",
            DocumentType = "bank_statement",
            File = BuildFormFile("bank-statement-v1.pdf", "First bank statement")
        }, CancellationToken.None);

        var uploadOneCreated = Assert.IsType<CreatedResult>(uploadOne.Result);
        Assert.Equal(201, uploadOneCreated.StatusCode);

        var document = await db.Documents.SingleAsync();
        Assert.Equal("uploaded", document.Status);
        Assert.Equal(1, document.CurrentVersionNumber);

        var packAfterUpload = await db.MonthlyPacks.SingleAsync();
        var slotAfterUpload = await db.DocumentSlots.SingleAsync();
        Assert.Equal("in_progress", packAfterUpload.Status);
        Assert.Equal("uploaded", slotAfterUpload.Status);

        var accountantController = new DocumentsController(db, storage)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_acc_001", "accountant"))
        };

        var getResult = await accountantController.GetById(document.Id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(getResult.Result);

        var rejectResult = await accountantController.Review(
            document.Id,
            new AddReviewDecisionRequest("rejected", "The closing balance is cut off.", null),
            CancellationToken.None);

        var rejectOk = Assert.IsType<OkObjectResult>(rejectResult.Result);
        var rejectJson = JsonSerializer.Serialize(rejectOk.Value);
        Assert.Contains("\"documentStatus\":\"rejected\"", rejectJson);

        var rejectedDocument = await db.Documents.SingleAsync();
        Assert.Equal("rejected", rejectedDocument.Status);
        var reuploadRequest = await db.Requests.SingleAsync();
        Assert.Equal("reupload_required", reuploadRequest.RequestType);
        Assert.Equal("waiting_on_client", reuploadRequest.Status);
        Assert.Equal(document.Id, reuploadRequest.RelatedDocumentId);

        var clientNotifications = await db.Notifications.Where(x => x.UserId == "u_client_001").ToListAsync();
        Assert.Contains(clientNotifications, x => x.Type == "document.rejected");

        var clientReuploadController = new DocumentsController(db, storage)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };

        var uploadTwo = await clientReuploadController.Upload(new UploadDocumentRequest
        {
            ClientId = "c_001",
            MonthlyPackId = "mp_001",
            DocumentSlotId = "slot_001",
            DocumentType = "bank_statement",
            DocumentId = document.Id,
            File = BuildFormFile("bank-statement-v2.pdf", "Corrected bank statement")
        }, CancellationToken.None);

        Assert.IsType<CreatedResult>(uploadTwo.Result);

        var finalDocument = await db.Documents.SingleAsync();
        Assert.Equal("uploaded", finalDocument.Status);
        Assert.Equal(2, finalDocument.CurrentVersionNumber);

        var requestController = new RequestsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };
        var commentResult = await requestController.AddComment(reuploadRequest.Id, new AddRequestCommentRequest("Corrected version uploaded."));
        Assert.IsType<OkObjectResult>(commentResult.Result);

        reuploadRequest = await db.Requests.SingleAsync();
        Assert.Equal("waiting_on_accountant", reuploadRequest.Status);

        var versionsResult = await clientReuploadController.GetVersions(document.Id, CancellationToken.None);
        var versionsOk = Assert.IsType<OkObjectResult>(versionsResult.Result);
        var versionsJson = JsonSerializer.Serialize(versionsOk.Value);
        Assert.Contains("\"VersionNumber\":2", versionsJson);
        Assert.Contains("\"VersionNumber\":1", versionsJson);
        Assert.Contains("\"isCurrent\":true", versionsJson);

        var finalPack = await db.MonthlyPacks.SingleAsync();
        var finalSlot = await db.DocumentSlots.SingleAsync();
        Assert.Equal("reopened", finalPack.Status);
        Assert.Equal("uploaded", finalSlot.Status);

        var accountantRequestController = new RequestsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_acc_001", "accountant"))
        };
        var resolveResult = await accountantRequestController.Resolve(reuploadRequest.Id, new ResolveRequestRequest("Corrected document received."));
        Assert.IsType<OkObjectResult>(resolveResult.Result);

        reuploadRequest = await db.Requests.SingleAsync();
        Assert.Equal("resolved", reuploadRequest.Status);

        var auditActions = await db.AuditLogs
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Action)
            .ToListAsync();

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

        var clientController = new DocumentsController(db, storage)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };

        var upload = await clientController.Upload(new UploadDocumentRequest
        {
            ClientId = "c_001",
            MonthlyPackId = "mp_001",
            DocumentSlotId = "slot_001",
            DocumentType = "bank_statement",
            File = BuildFormFile("bank-statement.pdf", "Download me")
        }, CancellationToken.None);
        Assert.IsType<CreatedResult>(upload.Result);

        var document = await db.Documents.SingleAsync();

        var accountantController = new DocumentsController(db, storage)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_acc_001", "accountant"))
        };

        var requestReupload = await accountantController.RequestReupload(
            document.Id,
            new RequestReuploadRequest("Please upload the full statement with all pages.", null),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(requestReupload.Result);

        var auditActions = await db.AuditLogs.Select(x => x.Action).ToListAsync();
        Assert.Contains("documents.reupload_requested", auditActions);
        Assert.Contains("request.created", auditActions);

        var download = await clientController.Download(document.Id, CancellationToken.None);
        Assert.IsType<FileStreamResult>(download);

        var accessLogs = await db.DocumentAccessLogs
            .Where(x => x.DocumentId == document.Id)
            .OrderBy(x => x.AccessedAtUtc)
            .ToListAsync();
        Assert.Contains(accessLogs, x => x.Action == "download" && x.AccessedByRole == "client");
    }

    [Fact]
    public async Task ViewAndDownload_WriteDocumentAccessLogs()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new InMemoryFileStorage();

        var uploadController = new DocumentsController(db, storage)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };

        var upload = await uploadController.Upload(new UploadDocumentRequest
        {
            ClientId = "c_001",
            MonthlyPackId = "mp_001",
            DocumentSlotId = "slot_001",
            DocumentType = "bank_statement",
            File = BuildFormFile("bank-statement.pdf", "Audit me")
        }, CancellationToken.None);
        Assert.IsType<CreatedResult>(upload.Result);

        var document = await db.Documents.SingleAsync();

        var readerController = new DocumentsController(db, storage)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_acc_001", "accountant"))
        };

        var getResult = await readerController.GetById(document.Id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(getResult.Result);

        var downloadResult = await readerController.Download(document.Id, CancellationToken.None);
        Assert.IsType<FileStreamResult>(downloadResult);

        var accessLogs = await db.DocumentAccessLogs
            .Where(x => x.DocumentId == document.Id)
            .OrderBy(x => x.AccessedAtUtc)
            .ToListAsync();
        Assert.Contains(accessLogs, x => x.Action == "view" && x.AccessedByRole == "accountant");
        Assert.Contains(accessLogs, x => x.Action == "download" && x.AccessedByRole == "accountant");
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

    private static ClaimsPrincipal BuildUser(string userId, string role, IEnumerable<string>? clientIds = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role)
        };

        if (clientIds is not null)
        {
            claims.AddRange(clientIds.Select(x => new Claim("client_id", x)));
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
            new User
            {
                Id = "u_acc_001",
                Email = "acc@test.com",
                FullName = "Accountant",
                Role = "accountant",
                PasswordHash = "x",
                ClientIdsJson = "[]"
            },
            new User
            {
                Id = "u_client_001",
                Email = "client@test.com",
                FullName = "Client",
                Role = "client",
                PasswordHash = "x",
                ClientIdsJson = "[\"c_001\"]"
            });

        db.Clients.Add(new Client
        {
            Id = "c_001",
            Name = "Alpha",
            EntityType = "Pty Ltd",
            Status = "active",
            ComplianceHealth = 90,
            AssignedAccountantId = "u_acc_001",
            PrimaryContact = "A",
            Email = "a@test.com"
        });

        db.ClientAssignments.Add(new ClientAssignment
        {
            Id = "ca_001",
            AccountantUserId = "u_acc_001",
            ClientId = "c_001"
        });

        db.MonthlyPacks.Add(new MonthlyPack
        {
            Id = "mp_001",
            ClientId = "c_001",
            Year = 2026,
            Month = 6,
            Status = "draft"
        });

        db.DocumentSlots.Add(new DocumentSlot
        {
            Id = "slot_001",
            MonthlyPackId = "mp_001",
            ClientId = "c_001",
            Category = "bank_statement",
            Label = "Bank Statement",
            IsRequired = true,
            Status = "missing"
        });

        db.SaveChanges();
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

            return Task.FromResult<StoredFileContent?>(
                new StoredFileContent(new MemoryStream(bytes), "application/pdf"));
        }
    }
}
