using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Controllers;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SecureClientPortal.Backend.Tests;

public class Phase3RequestsCommentsTests
{
    [Fact]
    public async Task GetComments_RespectsScope()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountant = BuildUser("u_acc_001", "accountant");
        var okController = new RequestsController(db) { ControllerContext = BuildControllerContext(accountant) };
        var okResult = await okController.GetComments("req_001");
        var ok = Assert.IsType<OkObjectResult>(okResult.Result);
        var comments = Assert.IsAssignableFrom<IEnumerable<RequestComment>>(ok.Value);
        Assert.Single(comments);

        var blockedController = new RequestsController(db) { ControllerContext = BuildControllerContext(accountant) };
        var blocked = await blockedController.GetComments("req_002");
        Assert.IsType<ForbidResult>(blocked.Result);
    }

    [Fact]
    public async Task AddComment_FromClient_CreatesAccountantNotification()
    {
        await using var db = BuildDb();
        Seed(db);

        var client = BuildUser("u_client_001", "client", ["c_001"]);
        var controller = new RequestsController(db)
        {
            ControllerContext = BuildControllerContext(client)
        };

        var result = await controller.AddComment("req_001", new AddRequestCommentRequest("Need an update"));
        Assert.IsType<OkObjectResult>(result.Result);

        var notifications = await db.Notifications.Where(x => x.ClientId == "c_001").ToListAsync();
        Assert.Contains(notifications, x => x.UserId == "u_acc_001" && x.Type == "request.comment");
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
            .UseInMemoryDatabase($"phase3-test-{Guid.NewGuid():N}")
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

        db.Clients.AddRange(
            new Client
            {
                Id = "c_001",
                Name = "Alpha",
                EntityType = "Pty Ltd",
                Status = "active",
                ComplianceHealth = 90,
                AssignedAccountantId = "u_acc_001",
                PrimaryContact = "A",
                Email = "a@test.com"
            },
            new Client
            {
                Id = "c_002",
                Name = "Beta",
                EntityType = "Pty Ltd",
                Status = "active",
                ComplianceHealth = 80,
                AssignedAccountantId = "u_acc_002",
                PrimaryContact = "B",
                Email = "b@test.com"
            });

        db.ClientAssignments.Add(new ClientAssignment
        {
            Id = "ca_001",
            AccountantUserId = "u_acc_001",
            ClientId = "c_001"
        });

        db.Requests.AddRange(
            new RequestItem
            {
                Id = "req_001",
                ClientId = "c_001",
                Title = "R1",
                Description = "d1",
                Priority = "medium",
                Status = "open",
                RequestedByUserId = "u_acc_001"
            },
            new RequestItem
            {
                Id = "req_002",
                ClientId = "c_002",
                Title = "R2",
                Description = "d2",
                Priority = "medium",
                Status = "open",
                RequestedByUserId = "u_acc_002"
            });

        db.RequestComments.Add(new RequestComment
        {
            Id = "rc_001",
            RequestId = "req_001",
            ClientId = "c_001",
            AuthorUserId = "u_client_001",
            AuthorRole = "client",
            Message = "Please review"
        });

        db.SaveChanges();
    }
}
