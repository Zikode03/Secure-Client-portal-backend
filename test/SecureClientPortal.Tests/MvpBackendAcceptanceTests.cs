using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Requests;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class MvpBackendAcceptanceTests
{
    private static readonly Guid AdminUserId = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountantUserId = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("f3333333-3333-3333-3333-333333333333");
    private static readonly Guid ClientId = Guid.Parse("f4444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task AdminToClientToAccountant_Journey_CompletesIndependentSlotWorkflow()
    {
        await using var db = BuildDb();
        SeedRolesAndUsers(db);
        var storage = new InMemoryFileStorage();
        var workflow = DocumentWorkflowTestFactory.Create(db, storage);

        var adminClients = new ClientsController(new ClientService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AdminUserId, "admin"))
        };

        var createdClient = Client.Create(ClientId, "Acme Holdings", "Pty Ltd", "A. Client", "client@acme.test", ClientStatus.Active);
        createdClient.AssignAccountant(AccountantUserId);
        createdClient.UpdateComplianceHealth(92);

        var createClientResult = await adminClients.Create(createdClient, TestContext.Current.CancellationToken);
        var createClientCreated = Assert.IsType<CreatedAtActionResult>(createClientResult.Result);
        var createdClientPayload = Assert.IsType<Client>(createClientCreated.Value);
        Assert.Equal(ClientId, createdClientPayload.Id);

        var accountantAssignments = await db.ClientAssignments
            .Where(x => x.ClientId == ClientId && x.AccountantUserId == AccountantUserId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(accountantAssignments);

        var accountantMonthlyPacks = new MonthlyPacksController(new MonthlyPackService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantUserId, "accountant"))
        };

        var createPackResult = await accountantMonthlyPacks.Create(
            new CreateMonthlyPackRequest(ClientId, 2026, 7, "draft"),
            TestContext.Current.CancellationToken);
        var createPackCreated = Assert.IsType<CreatedResult>(createPackResult.Result);
        var pack = Assert.IsType<MonthlyPackResponse>(createPackCreated.Value);

        var accountantSlots = new DocumentSlotsController(new DocumentSlotService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantUserId, "accountant"))
        };

        var bankSlotResult = await accountantSlots.Create(
            new CreateDocumentSlotRequest(pack.Id, "bank_statement", "Bank Statement", true, null),
            TestContext.Current.CancellationToken);
        var bankSlotCreated = Assert.IsType<CreatedResult>(bankSlotResult.Result);
        var bankSlot = Assert.IsType<DocumentSlotResponse>(bankSlotCreated.Value);

        var invoiceSlotResult = await accountantSlots.Create(
            new CreateDocumentSlotRequest(pack.Id, "sales_invoices", "Sales Invoices", false, null),
            TestContext.Current.CancellationToken);
        var invoiceSlotCreated = Assert.IsType<CreatedResult>(invoiceSlotResult.Result);
        var invoiceSlot = Assert.IsType<DocumentSlotResponse>(invoiceSlotCreated.Value);

        var clientDocuments = new DocumentsController(workflow)
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientId]))
        };

        var uploadResult = await clientDocuments.Upload(new UploadDocumentRequest
        {
            ClientId = ClientId,
            MonthlyPackId = pack.Id,
            DocumentSlotId = bankSlot.Id,
            DocumentType = "bank_statement",
            File = BuildFormFile("bank-statement-v1.pdf", "bank statement v1")
        }, TestContext.Current.CancellationToken);
        Assert.IsType<CreatedResult>(uploadResult);

        var document = await db.Documents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, document.CurrentVersionNumber);

        var clientSlots = new DocumentSlotsController(new DocumentSlotService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientId]))
        };

        var submitResult = await clientSlots.Submit(bankSlot.Id.ToString(), TestContext.Current.CancellationToken);
        var submitOk = Assert.IsType<OkObjectResult>(submitResult.Result);
        var submittedSlot = Assert.IsType<DocumentSlotResponse>(submitOk.Value);
        Assert.Equal("submitted", submittedSlot.Status);

        var submittedPack = await db.MonthlyPacks.SingleAsync(x => x.Id == pack.Id, TestContext.Current.CancellationToken);
        Assert.Equal("partially_submitted", submittedPack.Status);

        var reviewQueue = new ReviewQueueController(new ReviewQueueService(db), workflow)
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantUserId, "accountant"))
        };

        var pendingResult = await reviewQueue.GetPending(
            new ReviewQueueFilterRequest(ClientId, "bank_statement", "submitted", null, null, "newest"),
            TestContext.Current.CancellationToken);
        var pendingOk = Assert.IsType<OkObjectResult>(pendingResult.Result);
        var queueItems = Assert.IsAssignableFrom<IEnumerable<ReviewQueueItemResponse>>(pendingOk.Value).ToList();
        Assert.Single(queueItems);
        Assert.Equal(document.Id, queueItems[0].DocumentId);
        Assert.Equal(ClientId, queueItems[0].ClientId);

        var reuploadResult = await reviewQueue.RequestReupload(
            document.Id.ToString(),
            new RequestReuploadRequest("Final page is missing.", "Please re-upload the full statement."),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(reuploadResult);

        var request = await db.Requests.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("reupload_required", request.RequestType);
        Assert.Equal("waiting_on_client", request.Status);
        Assert.Equal(document.Id, request.RelatedDocumentId);

        var clientNotifications = await db.Notifications
            .Where(x => x.UserId == ClientUserId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(clientNotifications, x => x.Type == "document.reupload_requested");

        var clientRequests = new RequestsController(RequestService.CreateForTests(db, workflow))
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientId]))
        };

        var workspaceResult = await clientRequests.GetWorkspace(request.Id.ToString(), TestContext.Current.CancellationToken);
        var workspaceOk = Assert.IsType<OkObjectResult>(workspaceResult);
        var workspace = Assert.IsType<RequestWorkspaceResponse>(workspaceOk.Value);
        Assert.True(workspace.CanUploadCorrection);
        Assert.NotNull(workspace.RelatedDocument);
        Assert.Single(workspace.RelatedDocument!.Versions);

        var correctionUploadResult = await clientRequests.UploadDocument(
            request.Id.ToString(),
            new UploadRequestDocumentRequest
            {
                File = BuildFormFile("bank-statement-v2.pdf", "bank statement v2"),
                Message = "Uploaded the corrected full statement."
            },
            TestContext.Current.CancellationToken);
        var correctionUploadOk = Assert.IsType<OkObjectResult>(correctionUploadResult);
        var correctionUpload = Assert.IsType<RequestDocumentUploadResponse>(correctionUploadOk.Value);
        Assert.Equal("waiting_on_accountant", correctionUpload.Workspace.Request.Status);
        Assert.Equal(2, correctionUpload.Workspace.RelatedDocument!.CurrentVersionNumber);

        var approveResult = await reviewQueue.Review(
            document.Id.ToString(),
            new AddReviewDecisionRequest("accepted", "Looks correct now.", "Approved after re-upload."),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(approveResult);

        var finalDocument = await db.Documents.SingleAsync(x => x.Id == document.Id, TestContext.Current.CancellationToken);
        var finalBankSlot = await db.DocumentSlots.SingleAsync(x => x.Id == bankSlot.Id, TestContext.Current.CancellationToken);
        var finalInvoiceSlot = await db.DocumentSlots.SingleAsync(x => x.Id == invoiceSlot.Id, TestContext.Current.CancellationToken);
        var finalPack = await db.MonthlyPacks.SingleAsync(x => x.Id == pack.Id, TestContext.Current.CancellationToken);
        var versions = await db.DocumentVersions
            .Where(x => x.DocumentId == document.Id)
            .OrderBy(x => x.VersionNumber)
            .ToListAsync(TestContext.Current.CancellationToken);
        var auditActions = await db.AuditLogs
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Action)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("accepted", finalDocument.Status);
        Assert.Equal(2, finalDocument.CurrentVersionNumber);
        Assert.Equal("accepted", finalBankSlot.Status);
        Assert.Equal("not_started", finalInvoiceSlot.Status);
        Assert.Equal("complete", finalPack.Status);
        Assert.Equal(2, versions.Count);
        Assert.Contains(auditActions, x => x == "clients.created");
        Assert.Contains(auditActions, x => x == "documents.uploaded");
        Assert.Contains(auditActions, x => x == "document_slots.submitted");
        Assert.Contains(auditActions, x => x == "documents.reupload_requested");
        Assert.Contains(auditActions, x => x == "request.created");
        Assert.Contains(auditActions, x => x == "request.document_uploaded");
        Assert.Contains(auditActions, x => x == "documents.accepted");
        Assert.Contains(auditActions, x => x == "notification.sent");
    }

    [Fact]
    public async Task UploadRejectsUnsupportedFileTypeAndOversizedFiles()
    {
        await using var db = BuildDb();
        SeedRolesAndUsers(db);
        SeedClientPackAndSlot(db);
        var storage = new InMemoryFileStorage();
        var clientDocuments = new DocumentsController(DocumentWorkflowTestFactory.Create(db, storage))
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientId]))
        };

        var invalidTypeResult = await clientDocuments.Upload(new UploadDocumentRequest
        {
            ClientId = ClientId,
            MonthlyPackId = SeededMonthlyPackId,
            DocumentSlotId = SeededBankSlotId,
            DocumentType = "bank_statement",
            File = BuildFormFile("statement.exe", "bad file", "application/octet-stream")
        }, TestContext.Current.CancellationToken);
        var invalidTypeBadRequest = Assert.IsType<BadRequestObjectResult>(invalidTypeResult);
        var invalidTypeJson = JsonSerializer.Serialize(invalidTypeBadRequest.Value);
        Assert.Contains("Unsupported file type", invalidTypeJson);

        var oversizedResult = await clientDocuments.Upload(new UploadDocumentRequest
        {
            ClientId = ClientId,
            MonthlyPackId = SeededMonthlyPackId,
            DocumentSlotId = SeededBankSlotId,
            DocumentType = "bank_statement",
            File = BuildFormFile("statement.pdf", "x", "application/pdf", DocumentValidators.MaxUploadFileSizeBytes + 1)
        }, TestContext.Current.CancellationToken);
        var oversizedBadRequest = Assert.IsType<BadRequestObjectResult>(oversizedResult);
        var oversizedJson = JsonSerializer.Serialize(oversizedBadRequest.Value);
        Assert.Contains("maximum upload size", oversizedJson);

        Assert.Empty(await db.Documents.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.DocumentVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static readonly Guid SeededMonthlyPackId = Guid.Parse("f5555555-5555-5555-5555-555555555555");
    private static readonly Guid SeededBankSlotId = Guid.Parse("f6666666-6666-6666-6666-666666666666");

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"mvp-acceptance-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void SeedRolesAndUsers(PortalDbContext db)
    {
        db.RoleDefinitions.AddRange(
            RoleDefinition.Create("admin", "Admin", "admin", RolePermissions.SerializePermissions(RolePermissions.ForRole("admin")), true),
            RoleDefinition.Create("accountant", "Accountant", "accountant", RolePermissions.SerializePermissions(RolePermissions.ForRole("accountant")), true),
            RoleDefinition.Create("client", "Client", "client", RolePermissions.SerializePermissions(RolePermissions.ForRole("client")), true));

        db.Users.AddRange(
            BuildActiveUser(AdminUserId, "Admin", "admin@test.com", UserRole.Admin),
            BuildActiveUser(AccountantUserId, "Accountant", "accountant@test.com", UserRole.Accountant),
            BuildActiveUser(ClientUserId, "Client", "client@test.com", UserRole.Client, [ClientId]));

        db.SaveChanges();
    }

    private static void SeedClientPackAndSlot(PortalDbContext db)
    {
        var client = Client.Create(ClientId, "Acme Holdings", "Pty Ltd", "A. Client", "client@acme.test", ClientStatus.Active);
        client.AssignAccountant(AccountantUserId);
        db.Clients.Add(client);
        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), AccountantUserId, ClientId));

        db.MonthlyPacks.Add(MonthlyPack.Create(SeededMonthlyPackId, ClientId, 2026, 7));
        db.DocumentSlots.Add(DocumentSlot.Create(
            SeededBankSlotId,
            SeededMonthlyPackId,
            ClientId,
            "bank_statement",
            "Bank Statement",
            true,
            null));

        db.SaveChanges();
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

    private static User BuildActiveUser(Guid id, string fullName, string email, UserRole role, IEnumerable<Guid>? clientIds = null)
    {
        var user = User.CreateInvited(
            id,
            fullName,
            email,
            role,
            "hash",
            JsonSerializer.Serialize(clientIds?.Select(x => x.ToString()).ToArray() ?? Array.Empty<string>()),
            null);
        user.CompleteSetup(fullName, "hash");
        return user;
    }

    private static FormFile BuildFormFile(string fileName, string content, string contentType = "application/pdf", long? lengthOverride = null)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, lengthOverride ?? bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
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
