using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Controllers;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Documents;
using SecureClientPortal.Backend.Infrastructure.FirmManagement;
using SecureClientPortal.Backend.Infrastructure.FirmManagement.Application;
using SecureClientPortal.Backend.Infrastructure.Identity.Application;
using SecureClientPortal.Backend.Infrastructure.Requests.Application;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class AuthorizationScopeTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountantOneId = Guid.Parse("22222222-2222-2222-2222-222222222221");
    private static readonly Guid AccountantTwoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientUserId = Guid.Parse("33333333-3333-3333-3333-333333333331");
    private static readonly Guid ClientTwoUserId = Guid.Parse("33333333-3333-3333-3333-333333333332");
    private static readonly Guid ClientAlphaId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid ClientBetaId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    private static readonly Guid DocumentAlphaId = Guid.Parse("44444444-4444-4444-4444-444444444441");
    private static readonly Guid DocumentBetaId = Guid.Parse("44444444-4444-4444-4444-444444444442");
    private static readonly Guid RequestAlphaId = Guid.Parse("55555555-5555-5555-5555-555555555551");
    private static readonly Guid RequestBetaId = Guid.Parse("55555555-5555-5555-5555-555555555552");
    private static readonly Guid TaskAlphaId = Guid.Parse("66666666-6666-6666-6666-666666666661");
    private static readonly Guid TaskBetaId = Guid.Parse("66666666-6666-6666-6666-666666666662");

    [Fact]
    public async Task GetAllEndpoints_AdminSeesAll_AccountantSeesAssigned_ClientSeesOwn()
    {
        await using var db = BuildDb();
        Seed(db);

        var adminUser = BuildUser(AdminUserId, "admin");
        var accountantUser = BuildUser(AccountantOneId, "accountant");
        var clientUser = BuildUser(ClientUserId, "client", [ClientAlphaId]);

        var adminClients = await GetClientsCount(db, adminUser);
        var accountantClients = await GetClientsCount(db, accountantUser);
        var clientClients = await GetClientsCount(db, clientUser);

        Assert.Equal(2, adminClients);
        Assert.Equal(1, accountantClients);
        Assert.Equal(1, clientClients);

        var adminDocuments = await GetDocumentsCount(db, adminUser);
        var accountantDocuments = await GetDocumentsCount(db, accountantUser);
        var clientDocuments = await GetDocumentsCount(db, clientUser);

        Assert.Equal(2, adminDocuments);
        Assert.Equal(1, accountantDocuments);
        Assert.Equal(1, clientDocuments);

        var adminRequests = await GetRequestsCount(db, adminUser);
        var accountantRequests = await GetRequestsCount(db, accountantUser);
        var clientRequests = await GetRequestsCount(db, clientUser);

        Assert.Equal(2, adminRequests);
        Assert.Equal(1, accountantRequests);
        Assert.Equal(1, clientRequests);

        var adminTasks = await GetTasksCount(db, adminUser);
        var accountantTasks = await GetTasksCount(db, accountantUser);
        var clientTasks = await GetTasksCount(db, clientUser);

        Assert.Equal(2, adminTasks);
        Assert.Equal(1, accountantTasks);
        Assert.Equal(1, clientTasks);
    }

    [Fact]
    public async Task WriteEndpoints_EnforceClientScope()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountantUser = BuildUser(AccountantOneId, "accountant");
        var accountantController = new DocumentsController(new DocumentWorkflowService(db, new TestFileStorage()))
        {
            ControllerContext = BuildControllerContext(accountantUser)
        };

        var forbiddenDoc = await accountantController.Create(
            CreateDocument(DocumentBetaId, ClientBetaId, AccountantOneId, "Forbidden"),
            TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(forbiddenDoc);

        var clientUser = BuildUser(ClientUserId, "client", [ClientAlphaId]);
        var requestsController = new RequestsController(new RequestService(db))
        {
            ControllerContext = BuildControllerContext(clientUser)
        };

        var forbiddenRequest = await requestsController.Create(
            new CreateRequestRequest(
                ClientBetaId,
                "clarification_needed",
                "Bad",
                "Bad",
                "medium",
                null,
                null),
            TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(forbiddenRequest.Result);

        var taskController = new TasksController(new TaskService(db))
        {
            ControllerContext = BuildControllerContext(accountantUser)
        };

        var forbiddenTask = await taskController.Create(
            TaskItem.Create(Guid.NewGuid(), ClientBetaId, "Forbidden task", "todo", "medium", null, AccountantOneId),
            TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(forbiddenTask.Result);
    }

    [Fact]
    public async Task TaskEndpoints_RespectVisibilityAcrossReadUpdateAndDelete()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountantController = new TasksController(new TaskService(db))
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };

        var forbiddenRead = await accountantController.GetById(TaskBetaId.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(forbiddenRead.Result);

        var forbiddenUpdate = await accountantController.Update(
            TaskBetaId.ToString(),
            TaskItem.Create(Guid.NewGuid(), ClientBetaId, "Nope", "done", "high", null, AccountantOneId),
            TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(forbiddenUpdate.Result);

        var forbiddenDelete = await accountantController.Delete(TaskBetaId.ToString(), TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(forbiddenDelete);
    }

    [Fact]
    public async Task MeEndpoint_ReturnsScopedProfileAndPermissions()
    {
        await using var db = BuildDb();
        Seed(db);

        var service = new AuthService(
            db,
            Options.Create(new JwtOptions
            {
                SigningKey = "test-signing-key-test-signing-key",
                Audience = "test",
                Issuer = "test",
                ExpiresMinutes = 60
            }),
            new FakeAccessEmailSender(),
            new FakeAccessLinkBuilder());

        var controller = new AuthController(service)
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientAlphaId]))
        };

        var result = await controller.Me(TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.Contains("\"role\":\"client\"", json);
        Assert.Contains(ClientAlphaId.ToString(), json);
        Assert.Contains("\"permissions\"", json);
        Assert.Contains("clients.read", json);
        Assert.Contains("assignments.read", json);
    }

    [Fact]
    public async Task AssignmentsEndpoint_RespectsVisibilityAndPrimaryFlag()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountantController = new AssignmentsController(new AssignmentService(db), new CurrentUserContextFactory())
        {
            ControllerContext = BuildControllerContext(BuildUser(AccountantOneId, "accountant"))
        };
        var accountantResult = await accountantController.GetAll();
        var accountantOk = Assert.IsType<OkObjectResult>(accountantResult.Result);
        var accountantJson = JsonSerializer.Serialize(accountantOk.Value);
        Assert.Contains(ClientAlphaId.ToString(), accountantJson);
        Assert.DoesNotContain(ClientBetaId.ToString(), accountantJson);
        Assert.Contains("\"isPrimary\":true", accountantJson);

        var clientController = new AssignmentsController(new AssignmentService(db), new CurrentUserContextFactory())
        {
            ControllerContext = BuildControllerContext(BuildUser(ClientUserId, "client", [ClientAlphaId]))
        };
        var clientResult = await clientController.GetAll();
        var clientOk = Assert.IsType<OkObjectResult>(clientResult.Result);
        var clientJson = JsonSerializer.Serialize(clientOk.Value);
        Assert.Contains(ClientAlphaId.ToString(), clientJson);
        Assert.DoesNotContain(ClientBetaId.ToString(), clientJson);
    }

    private static async Task<int> GetClientsCount(PortalDbContext db, ClaimsPrincipal user)
    {
        var controller = new ClientsController(new ClientService(db))
        {
            ControllerContext = BuildControllerContext(user)
        };

        var result = await controller.GetAll(TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsAssignableFrom<IEnumerable<Client>>(ok.Value);
        return data.Count();
    }

    private static async Task<int> GetDocumentsCount(PortalDbContext db, ClaimsPrincipal user)
    {
        var controller = new DocumentsController(new DocumentWorkflowService(db, new TestFileStorage()))
        {
            ControllerContext = BuildControllerContext(user)
        };

        var result = await controller.GetAll(TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsAssignableFrom<IEnumerable<Document>>(ok.Value);
        return data.Count();
    }

    private static async Task<int> GetRequestsCount(PortalDbContext db, ClaimsPrincipal user)
    {
        var controller = new RequestsController(new RequestService(db))
        {
            ControllerContext = BuildControllerContext(user)
        };

        var result = await controller.GetAll(TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsAssignableFrom<IEnumerable<RequestItem>>(ok.Value);
        return data.Count();
    }

    private static async Task<int> GetTasksCount(PortalDbContext db, ClaimsPrincipal user)
    {
        var controller = new TasksController(new TaskService(db))
        {
            ControllerContext = BuildControllerContext(user)
        };

        var result = await controller.GetAll(TestContext.Current.CancellationToken);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsAssignableFrom<IEnumerable<TaskItem>>(ok.Value);
        return data.Count();
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
            .UseInMemoryDatabase($"scope-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
        SeedRoles(db);

        db.Users.AddRange(
            BuildActiveUser(AdminUserId, "Admin", "admin@test.com", "admin"),
            BuildActiveUser(AccountantOneId, "Accountant One", "acc1@test.com", "accountant"),
            BuildActiveUser(AccountantTwoId, "Accountant Two", "acc2@test.com", "accountant"),
            BuildActiveUser(ClientUserId, "Client User", "client1@test.com", "client", [ClientAlphaId]),
            BuildActiveUser(ClientTwoUserId, "Client Two", "client2@test.com", "client", [ClientBetaId]));

        var alpha = Client.Create(ClientAlphaId, "Alpha", "Pty Ltd", "A", "a@test.com", ClientStatus.Active);
        alpha.AssignAccountant(AccountantOneId);
        alpha.UpdateComplianceHealth(90);

        var beta = Client.Create(ClientBetaId, "Beta", "Pty Ltd", "B", "b@test.com", ClientStatus.Active);
        beta.AssignAccountant(AccountantTwoId);
        beta.UpdateComplianceHealth(80);

        db.Clients.AddRange(alpha, beta);
        db.ClientAssignments.AddRange(
            ClientAssignment.Create(Guid.Parse("77777777-7777-7777-7777-777777777771"), AccountantOneId, ClientAlphaId),
            ClientAssignment.Create(Guid.Parse("77777777-7777-7777-7777-777777777772"), AccountantTwoId, ClientBetaId));

        db.Documents.AddRange(
            CreateDocument(DocumentAlphaId, ClientAlphaId, ClientUserId, "Doc 1"),
            CreateDocument(DocumentBetaId, ClientBetaId, ClientTwoUserId, "Doc 2"));

        db.Requests.AddRange(
            RequestItem.Create(RequestAlphaId, ClientAlphaId, "clarification_needed", null, "R1", "d1", RequestPriority.Medium, AccountantOneId, RequestStatus.Open, null),
            RequestItem.Create(RequestBetaId, ClientBetaId, "clarification_needed", null, "R2", "d2", RequestPriority.Medium, AccountantTwoId, RequestStatus.Open, null));

        db.Tasks.AddRange(
            TaskItem.Create(TaskAlphaId, ClientAlphaId, "Review Alpha", "todo", "medium", null, AccountantOneId),
            TaskItem.Create(TaskBetaId, ClientBetaId, "Review Beta", "todo", "high", null, AccountantTwoId));

        db.SaveChanges();
    }

    private static void SeedRoles(PortalDbContext db)
    {
        db.RoleDefinitions.AddRange(
            RoleDefinition.Create("admin", "Admin", "admin", RolePermissions.SerializePermissions(RolePermissions.ForRole("admin")), true),
            RoleDefinition.Create("accountant", "Accountant", "accountant", RolePermissions.SerializePermissions(RolePermissions.ForRole("accountant")), true),
            RoleDefinition.Create("client", "Client", "client", RolePermissions.SerializePermissions(RolePermissions.ForRole("client")), true));
    }

    private static User BuildActiveUser(Guid id, string fullName, string email, string role, IEnumerable<Guid>? clientIds = null)
    {
        var user = User.CreateInvited(
            id,
            fullName,
            email,
            ParseRole(role),
            "hash",
            JsonSerializer.Serialize(clientIds?.Select(x => x.ToString()).ToArray() ?? Array.Empty<string>()),
            null);
        user.CompleteSetup(fullName, "hash");
        return user;
    }

    private static UserRole ParseRole(string role) => role switch
    {
        "admin" => UserRole.Admin,
        "accountant" => UserRole.Accountant,
        _ => UserRole.Client
    };

    private static Document CreateDocument(Guid id, Guid clientId, Guid uploadedByUserId, string name)
    {
        return Document.CreateUploaded(
            id,
            clientId,
            Guid.NewGuid(),
            name,
            "invoices",
            null,
            "application/pdf",
            10,
            $"{clientId}/test.pdf",
            uploadedByUserId);
    }

    private sealed class TestFileStorage : IFileStorage
    {
        public Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default)
        {
            return Task.FromResult<StoredFileContent?>(null);
        }

        public Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
        {
            return Task.FromResult(new StoredFile($"{clientId}/test.bin", file.FileName, file.FileName, "application/octet-stream", file.Length));
        }
    }

    private sealed class FakeAccessEmailSender : IAccessEmailSender
    {
        public Task<AccessEmailDispatchResult> SendInviteAsync(string recipientEmail, string recipientName, string setupUrl, DateTime expiresAtUtc, CancellationToken ct)
            => Task.FromResult(new AccessEmailDispatchResult("test"));

        public Task<AccessEmailDispatchResult> SendPasswordResetAsync(string recipientEmail, string recipientName, string setupUrl, DateTime expiresAtUtc, CancellationToken ct)
            => Task.FromResult(new AccessEmailDispatchResult("test"));
    }

    private sealed class FakeAccessLinkBuilder : IAccessLinkBuilder
    {
        public string BuildPasswordResetUrl(string email, string token) => $"https://example.test/reset?email={email}&token={token}";
        public string BuildSetupUrl(string email, string token) => $"https://example.test/setup?email={email}&token={token}";
    }
}

