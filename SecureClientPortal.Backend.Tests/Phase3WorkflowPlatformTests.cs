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

namespace SecureClientPortal.Backend.Tests;

public class Phase3WorkflowPlatformTests
{
    [Fact]
    public async Task Reject_RequestNotifyReplyReuploadResolve_CompletesWorkflow()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new InMemoryFileStorage();

        var clientDocuments = new DocumentsController(db, storage)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };

        var upload = await clientDocuments.Upload(new UploadDocumentRequest
        {
            ClientId = "c_001",
            MonthlyPackId = "mp_001",
            DocumentSlotId = "slot_001",
            DocumentType = "bank_statement",
            File = BuildFormFile("statement-v1.pdf", "bad statement")
        }, CancellationToken.None);
        Assert.IsType<CreatedResult>(upload.Result);

        var document = await db.Documents.SingleAsync();

        var accountantDocuments = new DocumentsController(db, storage)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_acc_001", "accountant"))
        };

        var reject = await accountantDocuments.Review(
            document.Id,
            new AddReviewDecisionRequest("rejected", "Please include all pages."),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(reject.Result);

        var request = await db.Requests.SingleAsync();
        Assert.Equal("reupload", request.RequestType);
        Assert.Equal(document.Id, request.RelatedDocumentId);

        var notificationsController = new NotificationsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };
        var notifications = await notificationsController.GetMine();
        var notificationsOk = Assert.IsType<OkObjectResult>(notifications.Result);
        var notificationItems = Assert.IsAssignableFrom<IEnumerable<Notification>>(notificationsOk.Value);
        Assert.Contains(notificationItems, x => x.Type == "document.rejected");

        var clientRequests = new RequestsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };
        var reply = await clientRequests.AddComment(request.Id, new AddRequestCommentRequest("Uploaded the corrected statement."));
        Assert.IsType<OkObjectResult>(reply.Result);

        var reupload = await clientDocuments.Upload(new UploadDocumentRequest
        {
            ClientId = "c_001",
            MonthlyPackId = "mp_001",
            DocumentSlotId = "slot_001",
            DocumentType = "bank_statement",
            DocumentId = document.Id,
            File = BuildFormFile("statement-v2.pdf", "good statement")
        }, CancellationToken.None);
        Assert.IsType<CreatedResult>(reupload.Result);

        var accountantRequests = new RequestsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_acc_001", "accountant"))
        };
        var resolve = await accountantRequests.Resolve(request.Id, new ResolveRequestRequest("All set."));
        Assert.IsType<OkObjectResult>(resolve.Result);

        var refreshedRequest = await db.Requests.SingleAsync();
        var refreshedDocument = await db.Documents.SingleAsync();
        Assert.Equal("resolved", refreshedRequest.Status);
        Assert.Equal(2, refreshedDocument.CurrentVersionNumber);

        var versions = await db.DocumentVersions
            .Where(x => x.DocumentId == document.Id)
            .OrderBy(x => x.VersionNumber)
            .ToListAsync();
        Assert.Equal(2, versions.Count);

        var markReadTarget = (await db.Notifications.FirstAsync(x => x.UserId == "u_client_001")).Id;
        var markRead = await notificationsController.MarkAsRead(markReadTarget);
        Assert.IsType<OkObjectResult>(markRead.Result);
        var markAll = await notificationsController.MarkAllRead();
        Assert.IsType<OkObjectResult>(markAll);
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
            .UseInMemoryDatabase($"phase3-platform-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            new User { Id = "u_acc_001", Email = "acc@test.com", FullName = "Accountant", Role = "accountant", PasswordHash = "x", ClientIdsJson = "[]" },
            new User { Id = "u_client_001", Email = "client@test.com", FullName = "Client", Role = "client", PasswordHash = "x", ClientIdsJson = "[\"c_001\"]" });

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

        db.ClientAssignments.Add(new ClientAssignment { Id = "ca_001", AccountantUserId = "u_acc_001", ClientId = "c_001" });
        db.MonthlyPacks.Add(new MonthlyPack { Id = "mp_001", ClientId = "c_001", Year = 2026, Month = 6, Status = "draft" });
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

        public async Task<StoredFileResult> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            var key = $"{clientId}/{Guid.NewGuid():N}-{file.FileName}";
            _files[key] = stream.ToArray();
            return new StoredFileResult(key, file.FileName, file.ContentType, file.Length);
        }

        public Task<StoredFileReadResult?> OpenReadAsync(string storageKey, CancellationToken ct = default)
        {
            if (!_files.TryGetValue(storageKey, out var bytes))
            {
                return Task.FromResult<StoredFileReadResult?>(null);
            }

            return Task.FromResult<StoredFileReadResult?>(new StoredFileReadResult(new MemoryStream(bytes), Path.GetFileName(storageKey), "application/pdf"));
        }
    }
}
