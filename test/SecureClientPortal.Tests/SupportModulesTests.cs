using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class SupportModulesTests
{
    private static readonly Guid AccountantOneId = Guid.Parse("d2222222-2222-2222-2222-222222222221");
    private static readonly Guid AccountantTwoId = Guid.Parse("d2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("d3333333-3333-3333-3333-333333333331");
    private static readonly Guid ClientAlphaId = Guid.Parse("daaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid ClientBetaId = Guid.Parse("daaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");

    [Fact]
    public async Task AuditLogs_RespectsAssignedClientScope_AndClientFilter()
    {
        await using var db = BuildDb();
        Seed(db);

        db.AuditLogs.AddRange(
            AuditLog.Create(
                Guid.Parse("d4444444-4444-4444-4444-444444444441"),
                AccountantOneId,
                "accountant",
                "document.view",
                "document",
                Guid.NewGuid(),
                ClientAlphaId,
                null,
                new DateTime(2026, 7, 11, 8, 0, 0, DateTimeKind.Utc)),
            AuditLog.Create(
                Guid.Parse("d4444444-4444-4444-4444-444444444442"),
                AccountantTwoId,
                "accountant",
                "document.view",
                "document",
                Guid.NewGuid(),
                ClientBetaId,
                null,
                new DateTime(2026, 7, 12, 8, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = new AuditLogsController(new AuditLogService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };

        var allVisible = await controller.GetAll(null, 200, TestContext.Current.CancellationToken);
        var visibleOk = Assert.IsType<OkObjectResult>(allVisible.Result);
        var visibleItems = Assert.IsAssignableFrom<IEnumerable<AuditLog>>(visibleOk.Value).ToList();
        Assert.Single(visibleItems);
        Assert.All(visibleItems, item => Assert.Equal(ClientAlphaId, item.ClientId));

        var forbidden = await controller.GetAll(ClientBetaId.ToString(), 200, TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(forbidden.Result);

        var scoped = await controller.GetAll(ClientAlphaId.ToString(), 200, TestContext.Current.CancellationToken);
        var scopedOk = Assert.IsType<OkObjectResult>(scoped.Result);
        var scopedItems = Assert.IsAssignableFrom<IEnumerable<AuditLog>>(scopedOk.Value).ToList();
        Assert.Single(scopedItems);
        Assert.Equal(ClientAlphaId, scopedItems[0].ClientId);
    }

    [Fact]
    public async Task Notifications_MarkReadAndMarkAllRead_RespectOwnership_AndWriteAuditLogs()
    {
        await using var db = BuildDb();
        Seed(db);

        var ownedUnread = Notification.Create(
            Guid.Parse("d5555555-5555-5555-5555-555555555551"),
            ClientUserId,
            ClientAlphaId,
            "document.rejected",
            "Document rejected",
            "Please re-upload.",
            "/documents/1",
            new DateTime(2026, 7, 11, 8, 0, 0, DateTimeKind.Utc));
        var ownedUnreadTwo = Notification.Create(
            Guid.Parse("d5555555-5555-5555-5555-555555555552"),
            ClientUserId,
            ClientAlphaId,
            "request.created",
            "New request",
            "Please respond.",
            "/requests/1",
            new DateTime(2026, 7, 12, 8, 0, 0, DateTimeKind.Utc));
        var foreign = Notification.Create(
            Guid.Parse("d5555555-5555-5555-5555-555555555553"),
            AccountantTwoId,
            ClientBetaId,
            "document.accepted",
            "Accepted",
            "Done.",
            "/documents/2",
            new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc));

        db.Notifications.AddRange(ownedUnread, ownedUnreadTwo, foreign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var controller = new NotificationsController(new NotificationService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientAlphaId]))
        };

        var mine = await controller.GetMine(TestContext.Current.CancellationToken);
        var mineOk = Assert.IsType<OkObjectResult>(mine.Result);
        var mineItems = Assert.IsAssignableFrom<IEnumerable<Notification>>(mineOk.Value).ToList();
        Assert.Equal(2, mineItems.Count);
        Assert.All(mineItems, item => Assert.Equal(ClientUserId, item.UserId));

        var readOne = await controller.MarkAsRead(ownedUnread.Id.ToString(), TestContext.Current.CancellationToken);
        var readOk = Assert.IsType<OkObjectResult>(readOne.Result);
        var readItem = Assert.IsType<Notification>(readOk.Value);
        Assert.True(readItem.IsRead);

        var missingForeign = await controller.MarkAsRead(foreign.Id.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<NotFoundResult>(missingForeign.Result);

        var markAll = await controller.MarkAllRead(TestContext.Current.CancellationToken);
        var markAllOk = Assert.IsType<OkObjectResult>(markAll);
        var payload = JsonSerializer.Serialize(markAllOk.Value);
        Assert.Contains("\"updated\":1", payload);

        var refreshedOwned = await db.Notifications
            .Where(x => x.UserId == ClientUserId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(refreshedOwned, item => Assert.True(item.IsRead));

        var auditActions = await db.AuditLogs
            .Where(x => x.ActorUserId == ClientUserId)
            .OrderBy(x => x.Action)
            .Select(x => x.Action)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains("notification.read", auditActions);
        Assert.Contains("notification.read_all", auditActions);
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
            .UseInMemoryDatabase($"support-modules-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        db.Users.AddRange(
            BuildActiveUser(AccountantOneId, "Accountant One", "acc1@test.com", UserRole.Accountant),
            BuildActiveUser(AccountantTwoId, "Accountant Two", "acc2@test.com", UserRole.Accountant),
            BuildActiveUser(ClientUserId, "Client User", "client@test.com", UserRole.Client, [ClientAlphaId]));

        var alpha = Client.Create(ClientAlphaId, "Alpha", "Pty Ltd", "Alpha Contact", "alpha@test.com", ClientStatus.Active);
        alpha.AssignAccountant(AccountantOneId);

        var beta = Client.Create(ClientBetaId, "Beta", "Pty Ltd", "Beta Contact", "beta@test.com", ClientStatus.Active);
        beta.AssignAccountant(AccountantTwoId);

        db.Clients.AddRange(alpha, beta);
        db.ClientAssignments.AddRange(
            ClientAssignment.Create(Guid.Parse("d6666666-6666-6666-6666-666666666661"), AccountantOneId, ClientAlphaId),
            ClientAssignment.Create(Guid.Parse("d6666666-6666-6666-6666-666666666662"), AccountantTwoId, ClientBetaId));

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
}
