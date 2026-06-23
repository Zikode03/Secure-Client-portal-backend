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

public class Phase3WorkflowPlatformTests
{
    private static readonly Guid AccountantUserId = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("b3333333-3333-3333-3333-333333333333");
    private static readonly Guid ClientAlphaId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    private static readonly Guid MonthlyPackId = Guid.Parse("b4444444-4444-4444-4444-444444444444");
    private static readonly Guid SlotId = Guid.Parse("b5555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task Reject_RequestNotifyReplyReuploadResolve_CompletesWorkflow()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new InMemoryFileStorage();

        var clientDocuments = new DocumentsController(new DocumentWorkflowService(db, storage))
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientAlphaId]))
        };

        var upload = await clientDocuments.Upload(new UploadDocumentRequest
        {
            ClientId = ClientAlphaId,
            MonthlyPackId = MonthlyPackId,
            DocumentSlotId = SlotId,
            DocumentType = "bank_statement",
            File = BuildFormFile("statement-v1.pdf", "bad statement")
        }, TestContext.Current.CancellationToken);
        Assert.IsType<CreatedResult>(upload);

        var document = await db.Documents.SingleAsync(TestContext.Current.CancellationToken);

        var accountantDocuments = new DocumentsController(new DocumentWorkflowService(db, storage))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantUserId, "accountant"))
        };

        var reject = await accountantDocuments.Review(
            document.Id.ToString(),
            new AddReviewDecisionRequest("rejected", "Please include all pages.", null),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(reject);

        var request = await db.Requests.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("reupload_required", request.RequestType);
        Assert.Equal(document.Id, request.RelatedDocumentId);

        var notificationsController = new NotificationsController(new NotificationService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientAlphaId]))
        };
        var notifications = await notificationsController.GetMine(TestContext.Current.CancellationToken);
        var notificationsOk = Assert.IsType<OkObjectResult>(notifications.Result);
        var notificationItems = Assert.IsAssignableFrom<IEnumerable<Notification>>(notificationsOk.Value);
        Assert.Contains(notificationItems, x => x.Type == "document.rejected");

        var clientRequests = new RequestsController(new RequestService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientAlphaId]))
        };
        var reply = await clientRequests.AddComment(
            request.Id.ToString(),
            new AddRequestCommentRequest("Uploaded the corrected statement."),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(reply.Result);

        var reupload = await clientDocuments.Upload(new UploadDocumentRequest
        {
            ClientId = ClientAlphaId,
            MonthlyPackId = MonthlyPackId,
            DocumentSlotId = SlotId,
            DocumentType = "bank_statement",
            DocumentId = document.Id,
            File = BuildFormFile("statement-v2.pdf", "good statement")
        }, TestContext.Current.CancellationToken);
        Assert.IsType<CreatedResult>(reupload);

        var accountantRequests = new RequestsController(new RequestService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantUserId, "accountant"))
        };
        var resolve = await accountantRequests.Resolve(
            request.Id.ToString(),
            new ResolveRequestRequest("All set."),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(resolve.Result);

        var refreshedRequest = await db.Requests.SingleAsync(TestContext.Current.CancellationToken);
        var refreshedDocument = await db.Documents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("resolved", refreshedRequest.Status);
        Assert.Equal(2, refreshedDocument.CurrentVersionNumber);

        var versions = await db.DocumentVersions
            .Where(x => x.DocumentId == document.Id)
            .OrderBy(x => x.VersionNumber)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);

        var markReadTarget = (await db.Notifications.FirstAsync(x => x.UserId == ClientUserId, TestContext.Current.CancellationToken)).Id;
        var markRead = await notificationsController.MarkAsRead(markReadTarget.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(markRead.Result);
        var markAll = await notificationsController.MarkAllRead(TestContext.Current.CancellationToken);
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
            .UseInMemoryDatabase($"phase3-platform-{Guid.NewGuid():N}")
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

        db.MonthlyPacks.Add(new MonthlyPack
        {
            Id = MonthlyPackId,
            ClientId = ClientAlphaId,
            Year = 2026,
            Month = 6
        });

        var slot = new DocumentSlot
        {
            Id = SlotId,
            MonthlyPackId = MonthlyPackId,
            ClientId = ClientAlphaId
        };
        slot.UpdateDefinition("bank_statement", "Bank Statement", true);
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


