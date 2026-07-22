using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class ReviewQueueModuleTests
{
    private static readonly Guid AccountantOneId = Guid.Parse("c2222222-2222-2222-2222-222222222221");
    private static readonly Guid AccountantTwoId = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("c3333333-3333-3333-3333-333333333333");
    private static readonly Guid ClientAlphaId = Guid.Parse("caaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid ClientBetaId = Guid.Parse("caaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");

    [Fact]
    public async Task PendingQueue_RespectsAssignedClientScope_AndSupportsWorkspaceFilters()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new InMemoryFileStorage();

        var alphaPack = MonthlyPack.Create(Guid.Parse("c4444444-4444-4444-4444-444444444441"), ClientAlphaId, 2026, 7);
        var betaPack = MonthlyPack.Create(Guid.Parse("c4444444-4444-4444-4444-444444444442"), ClientBetaId, 2026, 7);
        db.MonthlyPacks.AddRange(alphaPack, betaPack);

        var olderAlphaDocumentId = Guid.Parse("c5555555-5555-5555-5555-555555555551");
        var newerAlphaDocumentId = Guid.Parse("c5555555-5555-5555-5555-555555555552");
        var betaDocumentId = Guid.Parse("c5555555-5555-5555-5555-555555555553");

        var olderAlphaSlot = DocumentSlot.Create(
            Guid.Parse("c6666666-6666-6666-6666-666666666661"),
            alphaPack.Id,
            ClientAlphaId,
            "bank_statement",
            "Bank Statement",
            true,
            null);
        olderAlphaSlot.MarkDraft(olderAlphaDocumentId);
        olderAlphaSlot.Submit(AccountantOneId, DateTime.UtcNow.AddDays(-10));

        var newerAlphaSlot = DocumentSlot.Create(
            Guid.Parse("c6666666-6666-6666-6666-666666666662"),
            alphaPack.Id,
            ClientAlphaId,
            "sales_invoices",
            "Sales Invoices",
            true,
            null);
        newerAlphaSlot.MarkDraft(newerAlphaDocumentId);
        newerAlphaSlot.Submit(AccountantOneId, DateTime.UtcNow.AddDays(-1));
        newerAlphaSlot.MarkUnderReview();

        var betaSlot = DocumentSlot.Create(
            Guid.Parse("c6666666-6666-6666-6666-666666666663"),
            betaPack.Id,
            ClientBetaId,
            "payroll",
            "Payroll",
            true,
            null);
        betaSlot.MarkDraft(betaDocumentId);
        betaSlot.Submit(AccountantTwoId, DateTime.UtcNow.AddDays(-2));

        db.DocumentSlots.AddRange(olderAlphaSlot, newerAlphaSlot, betaSlot);
        db.Documents.AddRange(
            Document.CreateUploaded(
                olderAlphaDocumentId,
                ClientAlphaId,
                alphaPack.Id,
                "Alpha-Bank.pdf",
                "bank_statement",
                olderAlphaSlot.Id,
                "application/pdf",
                100,
                "alpha/bank.pdf",
                AccountantOneId),
            Document.CreateUploaded(
                newerAlphaDocumentId,
                ClientAlphaId,
                alphaPack.Id,
                "Alpha-Invoices.pdf",
                "sales_invoices",
                newerAlphaSlot.Id,
                "application/pdf",
                100,
                "alpha/invoices.pdf",
                AccountantOneId),
            Document.CreateUploaded(
                betaDocumentId,
                ClientBetaId,
                betaPack.Id,
                "Beta-Payroll.pdf",
                "payroll",
                betaSlot.Id,
                "application/pdf",
                100,
                "beta/payroll.pdf",
                AccountantTwoId));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = new ReviewQueueController(new ReviewQueueService(db), DocumentWorkflowTestFactory.Create(db, storage))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };

        var result = await controller.GetPending(new ReviewQueueFilterRequest(null, null, null, null, null, "newest"), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<ReviewQueueItemResponse>>(ok.Value).ToList();

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(ClientAlphaId, item.ClientId));
        Assert.Equal(newerAlphaDocumentId, items[0].DocumentId);
        Assert.Equal("under_review", items[0].SlotStatus);
        Assert.Equal("normal", items[0].ReviewPriority);
        Assert.Equal(olderAlphaDocumentId, items[1].DocumentId);
        Assert.Equal("submitted", items[1].SlotStatus);
        Assert.Equal("urgent", items[1].ReviewPriority);
        Assert.True(items[1].ReviewAgeDays >= 10);

        var categoryFiltered = await controller.GetPending(
            new ReviewQueueFilterRequest(null, "sales_invoices", null, null, null, "newest"),
            TestContext.Current.CancellationToken);
        var categoryOk = Assert.IsType<OkObjectResult>(categoryFiltered.Result);
        var categoryItems = Assert.IsAssignableFrom<IEnumerable<ReviewQueueItemResponse>>(categoryOk.Value).ToList();
        Assert.Single(categoryItems);
        Assert.Equal(newerAlphaDocumentId, categoryItems[0].DocumentId);

        var priorityFiltered = await controller.GetPending(
            new ReviewQueueFilterRequest(null, null, null, 7, "urgent", "oldest"),
            TestContext.Current.CancellationToken);
        var priorityOk = Assert.IsType<OkObjectResult>(priorityFiltered.Result);
        var priorityItems = Assert.IsAssignableFrom<IEnumerable<ReviewQueueItemResponse>>(priorityOk.Value).ToList();
        Assert.Single(priorityItems);
        Assert.Equal(olderAlphaDocumentId, priorityItems[0].DocumentId);

        var forbidden = await controller.GetPending(
            new ReviewQueueFilterRequest(ClientBetaId, null, null, null, null, "newest"),
            TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(forbidden.Result);
    }

    [Fact]
    public async Task Workspace_Actions_ExposeHistory_AndDriveCorrectionWorkflow()
    {
        await using var db = BuildDb();
        Seed(db);
        var storage = new InMemoryFileStorage();

        var monthlyPack = MonthlyPack.Create(Guid.Parse("c4444444-4444-4444-4444-444444444443"), ClientAlphaId, 2026, 7);
        db.MonthlyPacks.Add(monthlyPack);

        var documentId = Guid.Parse("c5555555-5555-5555-5555-555555555554");
        var documentVersionId = Guid.Parse("c8888888-8888-8888-8888-888888888881");
        var slotId = Guid.Parse("c6666666-6666-6666-6666-666666666664");
        var commentId = Guid.Parse("c9999999-9999-9999-9999-999999999991");
        var decisionId = Guid.Parse("d1111111-1111-1111-1111-111111111111");

        var slot = DocumentSlot.Create(
            slotId,
            monthlyPack.Id,
            ClientAlphaId,
            "bank_statement",
            "Bank Statement",
            true,
            null);
        slot.MarkDraft(documentId);
        slot.Submit(AccountantOneId, DateTime.UtcNow.AddDays(-4));
        db.DocumentSlots.Add(slot);

        var document = Document.CreateUploaded(
            documentId,
            ClientAlphaId,
            monthlyPack.Id,
            "Alpha-Bank.pdf",
            "bank_statement",
            slot.Id,
            "application/pdf",
            128,
            "alpha/bank-v1.pdf",
            ClientUserId);
        db.Documents.Add(document);
        db.DocumentVersions.Add(DocumentVersion.Create(
            documentVersionId,
            documentId,
            1,
            "Alpha-Bank.pdf",
            "Alpha-Bank.pdf",
            "alpha-bank-v1.pdf",
            "application/pdf",
            128,
            "alpha/bank-v1.pdf",
            true,
            ClientUserId));
        db.DocumentComments.Add(DocumentComment.Create(
            commentId,
            documentId,
            ClientUserId,
            "client",
            "Initial upload ready for review."));
        db.ReviewDecisions.Add(ReviewDecision.Create(
            decisionId,
            documentId,
            "under_review",
            AccountantOneId,
            "accountant",
            null,
            "Taking ownership of the review.",
            DateTime.UtcNow.AddDays(-2)));

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = new ReviewQueueController(new ReviewQueueService(db), DocumentWorkflowTestFactory.Create(db, storage))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };

        var workspaceResult = await controller.GetWorkspace(documentId.ToString(), TestContext.Current.CancellationToken);

        var workspaceOk = Assert.IsType<OkObjectResult>(workspaceResult);
        var workspace = Assert.IsType<ReviewQueueWorkspaceResponse>(workspaceOk.Value);
        Assert.Equal(documentId, workspace.Item.DocumentId);
        Assert.Equal("high", workspace.Item.ReviewPriority);
        Assert.Equal($"/api/documents/{documentId}/download", workspace.DownloadUrl);
        Assert.Single(workspace.Versions);
        Assert.Single(workspace.Comments);
        Assert.Single(workspace.ReviewHistory);

        var commentResult = await controller.AddComment(
            documentId.ToString(),
            new AddDocumentCommentRequest("Please include the missing summary page."),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(commentResult);

        var reuploadResult = await controller.RequestReupload(
            documentId.ToString(),
            new RequestReuploadRequest("The final page is missing from the statement.", "Need a complete submission."),
            TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(reuploadResult);

        var updatedDocument = await db.Documents.SingleAsync(x => x.Id == documentId, TestContext.Current.CancellationToken);
        var updatedSlot = await db.DocumentSlots.SingleAsync(x => x.Id == slotId, TestContext.Current.CancellationToken);
        var requests = await db.Requests.Where(x => x.RelatedDocumentId == documentId).ToListAsync(TestContext.Current.CancellationToken);
        var comments = await db.DocumentComments.Where(x => x.DocumentId == documentId).OrderBy(x => x.CreatedAtUtc).ToListAsync(TestContext.Current.CancellationToken);
        var decisions = await db.ReviewDecisions.Where(x => x.DocumentId == documentId).OrderByDescending(x => x.DecidedAtUtc).ToListAsync(TestContext.Current.CancellationToken);
        var notifications = await db.Notifications.Where(x => x.UserId == ClientUserId).ToListAsync(TestContext.Current.CancellationToken);
        var auditActions = await db.AuditLogs.Where(x => x.EntityId == documentId).Select(x => x.Action).ToListAsync(TestContext.Current.CancellationToken);
        var allAuditActions = await db.AuditLogs.Select(x => x.Action).ToListAsync(TestContext.Current.CancellationToken);
        var accessActions = await db.DocumentAccessLogs.Where(x => x.DocumentId == documentId).Select(x => x.Action).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("rejected", updatedDocument.Status);
        Assert.Equal("reupload_required", updatedSlot.Status);
        Assert.Contains(requests, x => x.RequestType == "reupload_required" && x.Status == "waiting_on_client");
        Assert.Equal(2, comments.Count);
        Assert.Equal("Please include the missing summary page.", comments[^1].Message);
        Assert.Contains(decisions, x => x.Decision == "request_reupload" && x.Reason == "The final page is missing from the statement.");
        Assert.Contains(notifications, x => x.Type == "document.reupload_requested");
        Assert.Contains(auditActions, x => x == "review_queue.opened");
        Assert.Contains(auditActions, x => x == "comment.added");
        Assert.Contains(auditActions, x => x == "documents.reupload_requested");
        Assert.Contains(allAuditActions, x => x == "request.created");
        Assert.Contains(accessActions, x => x == "review_queue_open");
    }

    private static ControllerContext BuildControllerContext(ClaimsPrincipal user)
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = user
            }
        };
    }

    private static ClaimsPrincipal BuildUser(Guid userId, string role)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        ], "test"));
    }

    private static PortalDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase($"review-queue-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            BuildActiveUser(AccountantOneId, "Accountant One", "acc1@test.com", UserRole.Accountant),
            BuildActiveUser(AccountantTwoId, "Accountant Two", "acc2@test.com", UserRole.Accountant),
            BuildActiveUser(ClientUserId, "Client User", "client.alpha@test.com", UserRole.Client, [ClientAlphaId]));

        var alpha = Client.Create(ClientAlphaId, "Alpha", "Pty Ltd", "Alpha Contact", "alpha@test.com", ClientStatus.Active);
        alpha.AssignAccountant(AccountantOneId);

        var beta = Client.Create(ClientBetaId, "Beta", "Pty Ltd", "Beta Contact", "beta@test.com", ClientStatus.Active);
        beta.AssignAccountant(AccountantTwoId);

        db.Clients.AddRange(alpha, beta);
        db.ClientAssignments.AddRange(
            ClientAssignment.Create(Guid.Parse("c7777777-7777-7777-7777-777777777771"), AccountantOneId, ClientAlphaId),
            ClientAssignment.Create(Guid.Parse("c7777777-7777-7777-7777-777777777772"), AccountantTwoId, ClientBetaId));

        db.SaveChanges();
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

    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default)
        {
            if (!_files.TryGetValue(storageKey, out var bytes))
            {
                return Task.FromResult<StoredFileContent?>(null);
            }

            return Task.FromResult<StoredFileContent?>(new StoredFileContent(new MemoryStream(bytes), "application/pdf"));
        }

        public async Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            var key = $"{clientId}/{Guid.NewGuid():N}-{file.FileName}";
            _files[key] = stream.ToArray();
            return new StoredFile(key, file.FileName, file.FileName, file.ContentType, file.Length);
        }
    }
}
