using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Domain.Modules.Documents.Services;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class MonthlyPacksModuleTests
{
    private static readonly Guid AccountantOneId = Guid.Parse("82222222-2222-2222-2222-222222222221");
    private static readonly Guid AccountantTwoId = Guid.Parse("82222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientAlphaId = Guid.Parse("8aaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid ClientBetaId = Guid.Parse("8aaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");

    [Fact]
    public async Task CreateAndList_RespectsAssignedClientScope()
    {
        await using var db = BuildDb();
        Seed(db);

        var controller = new MonthlyPacksController(new MonthlyPackService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };

        var createdResult = await controller.Create(
            new CreateMonthlyPackRequest(ClientAlphaId, 2026, 7, "draft"),
            TestContext.Current.CancellationToken);

        var created = Assert.IsType<CreatedResult>(createdResult.Result);
        var createdPack = Assert.IsType<MonthlyPackResponse>(created.Value);
        Assert.Equal(ClientAlphaId, createdPack.ClientId);
        Assert.Equal(2026, createdPack.Year);
        Assert.Equal(7, createdPack.Month);

        var visibleResult = await controller.GetAll(null, TestContext.Current.CancellationToken);
        var visibleOk = Assert.IsType<OkObjectResult>(visibleResult.Result);
        var visiblePacks = Assert.IsAssignableFrom<IEnumerable<MonthlyPackResponse>>(visibleOk.Value);
        Assert.Contains(visiblePacks, x => x.Id == createdPack.Id);
        Assert.DoesNotContain(visiblePacks, x => x.ClientId == ClientBetaId);
    }

    [Fact]
    public async Task BankStatementCanBeSubmittedWhileInvoicesAreMissing()
    {
        await using var db = BuildDb();
        Seed(db);

        var pack = MonthlyPack.Create(Guid.Parse("84444444-4444-4444-4444-444444444441"), ClientAlphaId, 2026, 8);
        db.MonthlyPacks.Add(pack);
        var documentId = Guid.Parse("8aaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee1");

        var bankStatementSlot = DocumentSlot.Create(
            Guid.Parse("85555555-5555-5555-5555-555555555551"),
            pack.Id,
            ClientAlphaId,
            "bank_statement",
            "Bank Statement",
            true,
            null);
        bankStatementSlot.MarkDraft(documentId);

        var invoicesSlot = DocumentSlot.Create(
            Guid.Parse("85555555-5555-5555-5555-555555555552"),
            pack.Id,
            ClientAlphaId,
            "sales_invoices",
            "Sales Invoices",
            true,
            null);
        invoicesSlot.MarkNotStarted();

        db.DocumentSlots.AddRange(bankStatementSlot, invoicesSlot);
        db.Documents.Add(Document.CreateUploaded(
            documentId,
            ClientAlphaId,
            pack.Id,
            "Bank Statement.pdf",
            "bank_statement",
            bankStatementSlot.Id,
            "application/pdf",
            25,
            "alpha/bank-statement.pdf",
            AccountantOneId));
        db.DocumentVersions.Add(DocumentVersion.Create(
            Guid.Parse("8aaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee2"),
            Guid.Parse("8aaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee1"),
            1,
            "Bank Statement.pdf",
            "Bank Statement.pdf",
            "bank-statement.pdf",
            "application/pdf",
            25,
            "alpha/bank-statement.pdf",
            true,
            AccountantOneId,
            DateTime.UtcNow));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = new DocumentSlotsController(new DocumentSlotService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };

        var submitResult = await controller.Submit(bankStatementSlot.Id.ToString(), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(submitResult.Result);
        var submittedSlot = Assert.IsType<DocumentSlotResponse>(ok.Value);
        Assert.Equal("submitted", submittedSlot.Status);
        Assert.Equal("partially_submitted", pack.Status);
        Assert.Equal("not_started", invoicesSlot.Status);
    }

    [Fact]
    public void OptionalOrNotApplicableSlots_DoNotBlockPackCompletion()
    {
        var pack = MonthlyPack.Create(Guid.NewGuid(), ClientAlphaId, 2026, 9);

        var requiredSlot = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "bank_statement", "Bank Statement", true, null);
        requiredSlot.Accept(Guid.NewGuid());

        var optionalSlot = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "expense_documents", "Expense Documents", false, null);
        optionalSlot.MarkNotApplicable();

        pack.RecalculateStatus([requiredSlot, optionalSlot]);

        Assert.Equal("complete", pack.Status);
    }

    [Fact]
    public void ReuploadRequiredSlot_DoesNotResetAcceptedSlot()
    {
        var pack = MonthlyPack.Create(Guid.NewGuid(), ClientAlphaId, 2026, 10);

        var acceptedBankStatement = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "bank_statement", "Bank Statement", true, null);
        acceptedBankStatement.Accept(Guid.NewGuid());

        var invoices = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "sales_invoices", "Sales Invoices", false, null);
        invoices.RequestReupload(Guid.NewGuid(), "Please correct the invoice batch.");

        pack.RecalculateStatus([acceptedBankStatement, invoices]);

        Assert.Equal("accepted", acceptedBankStatement.Status);
        Assert.Equal("reupload_required", invoices.Status);
        Assert.Equal("complete", pack.Status);
    }

    [Fact]
    public async Task DocumentSlots_GetByMonthlyPackId_EnforcesVisibility()
    {
        await using var db = BuildDb();
        Seed(db);

        var pack = MonthlyPack.Create(Guid.Parse("86666666-6666-6666-6666-666666666661"), ClientBetaId, 2026, 8);
        db.MonthlyPacks.Add(pack);
        db.DocumentSlots.Add(DocumentSlot.Create(
            Guid.Parse("87777777-7777-7777-7777-777777777771"),
            pack.Id,
            ClientBetaId,
            "invoices",
            "Invoices",
            true,
            null));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = new DocumentSlotsController(new DocumentSlotService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };

        var result = await controller.GetByMonthlyPackId(pack.Id.ToString(), TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task AccountantCanMarkOptionalSlotNotApplicable_WithoutBlockingRequiredSlotProgress()
    {
        await using var db = BuildDb();
        Seed(db);

        var pack = MonthlyPack.Create(Guid.NewGuid(), ClientAlphaId, 2026, 11);
        db.MonthlyPacks.Add(pack);

        var requiredSlot = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "bank_statement", "Bank Statement", true, null);
        var optionalSlot = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "expenses", "Expenses", false, null);
        db.DocumentSlots.AddRange(requiredSlot, optionalSlot);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = new DocumentSlotsController(new DocumentSlotService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };

        var result = await controller.MarkNotApplicable(optionalSlot.Id.ToString(), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var slot = Assert.IsType<DocumentSlotResponse>(ok.Value);
        Assert.Equal("not_applicable", slot.Status);
    }

    [Fact]
    public async Task AccountantCanClosePack_WhenAllApplicableRequiredSlotsAreAccepted()
    {
        await using var db = BuildDb();
        Seed(db);

        var pack = MonthlyPack.Create(Guid.NewGuid(), ClientAlphaId, 2026, 12);
        db.MonthlyPacks.Add(pack);

        var requiredSlot = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "bank_statement", "Bank Statement", true, null);
        requiredSlot.Accept(Guid.NewGuid());
        var optionalSlot = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "payroll", "Payroll", false, null);
        optionalSlot.MarkNotApplicable();
        db.DocumentSlots.AddRange(requiredSlot, optionalSlot);
        pack.RecalculateStatus([requiredSlot, optionalSlot]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = new MonthlyPacksController(new MonthlyPackService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };

        var result = await controller.Close(pack.Id.ToString(), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var closedPack = Assert.IsType<MonthlyPackResponse>(ok.Value);
        Assert.Equal("closed", closedPack.Status);
    }

    [Fact]
    public void MonthlyPackCloseIfReady_RejectsUnacceptedRequiredSlots()
    {
        var pack = MonthlyPack.Create(Guid.NewGuid(), ClientAlphaId, 2026, 12);
        var requiredSlot = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "bank_statement", "Bank Statement", true, null);
        requiredSlot.MarkDraft(Guid.NewGuid());

        var error = Assert.Throws<DomainRuleException>(() => pack.CloseIfReady([requiredSlot]));
        Assert.Equal("All applicable required slots must be accepted before the monthly pack can be closed.", error.Message);
    }

    [Fact]
    public void DocumentSubmissionDomainService_SubmitsOnlyCurrentVersionForLinkedSlot()
    {
        var pack = MonthlyPack.Create(Guid.NewGuid(), ClientAlphaId, 2026, 12);
        var slot = DocumentSlot.Create(Guid.NewGuid(), pack.Id, ClientAlphaId, "bank_statement", "Bank Statement", true, null);
        var document = Document.CreateUploaded(
            Guid.NewGuid(),
            ClientAlphaId,
            pack.Id,
            "Bank Statement.pdf",
            "bank_statement",
            slot.Id,
            "application/pdf",
            10,
            "alpha/bank.pdf",
            AccountantOneId);
        slot.MarkDraft(document.Id);
        var version = DocumentVersion.Create(
            Guid.NewGuid(),
            document.Id,
            1,
            "Bank Statement.pdf",
            "Bank Statement.pdf",
            "bank.pdf",
            "application/pdf",
            10,
            "alpha/bank.pdf",
            true,
            AccountantOneId,
            DateTime.UtcNow);

        var service = new DocumentSubmissionDomainService();

        service.Submit(document, version, slot, AccountantOneId, DateTime.UtcNow);

        Assert.Equal("submitted", slot.Status);
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
            .UseInMemoryDatabase($"monthly-packs-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            BuildActiveUser(AccountantOneId, "Accountant One", "acc1@test.com", UserRole.Accountant),
            BuildActiveUser(AccountantTwoId, "Accountant Two", "acc2@test.com", UserRole.Accountant));

        var alpha = Client.Create(ClientAlphaId, "Alpha", "Pty Ltd", "A", "a@test.com", ClientStatus.Active);
        alpha.AssignAccountant(AccountantOneId);

        var beta = Client.Create(ClientBetaId, "Beta", "Pty Ltd", "B", "b@test.com", ClientStatus.Active);
        beta.AssignAccountant(AccountantTwoId);

        db.Clients.AddRange(alpha, beta);
        db.ClientAssignments.AddRange(
            ClientAssignment.Create(Guid.Parse("88888888-8888-8888-8888-888888888881"), AccountantOneId, ClientAlphaId),
            ClientAssignment.Create(Guid.Parse("88888888-8888-8888-8888-888888888882"), AccountantTwoId, ClientBetaId));

        db.SaveChanges();
    }

    private static User BuildActiveUser(Guid id, string fullName, string email, UserRole role)
    {
        var user = User.CreateInvited(
            id,
            fullName,
            email,
            role,
            "hash",
            JsonSerializer.Serialize(Array.Empty<string>()),
            null);
        user.CompleteSetup(fullName, "hash");
        return user;
    }
}
