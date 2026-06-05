using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Controllers;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace SecureClientPortal.Backend.Tests;

public class AuthorizationScopeTests
{
    [Fact]
    public async Task GetAllEndpoints_AdminSeesAll_AccountantSeesAssigned_ClientSeesOwn()
    {
        await using var db = BuildDb();
        Seed(db);

        var adminUser = BuildUser("u_admin_001", "admin");
        var accountantUser = BuildUser("u_acc_001", "accountant");
        var clientUser = BuildUser("u_client_001", "client", ["c_001"]);

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

        var accountantUser = BuildUser("u_acc_001", "accountant");
        var accountantController = new DocumentsController(db, new TestFileStorage())
        {
            ControllerContext = BuildControllerContext(accountantUser)
        };

        var forbiddenDoc = await accountantController.Create(new Document
        {
            Id = "doc_forbidden",
            ClientId = "c_002",
            Name = "Forbidden",
            Category = "invoices",
            Status = "uploaded",
            SizeBytes = 10,
            UploadedByUserId = "u_acc_001"
        });
        Assert.IsType<ForbidResult>(forbiddenDoc.Result);

        var clientUser = BuildUser("u_client_001", "client", ["c_001"]);
        var requestsController = new RequestsController(db)
        {
            ControllerContext = BuildControllerContext(clientUser)
        };

        var forbiddenRequest = await requestsController.Create(new CreateRequestRequest(
            "c_002",
            "clarification_needed",
            "Bad",
            "Bad",
            "medium",
            null,
            null));
        Assert.IsType<ForbidResult>(forbiddenRequest.Result);

        var taskController = new TasksController(db)
        {
            ControllerContext = BuildControllerContext(accountantUser)
        };

        var forbiddenTask = await taskController.Create(new TaskItem
        {
            Id = "task_forbidden",
            ClientId = "c_002",
            Title = "Forbidden task",
            Status = "todo",
            Priority = "medium"
        });
        Assert.IsType<ForbidResult>(forbiddenTask.Result);
    }

    [Fact]
    public async Task TaskEndpoints_RespectVisibilityAcrossReadUpdateAndDelete()
    {
        await using var db = BuildDb();
        Seed(db);

        var accountantController = new TasksController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_acc_001", "accountant"))
        };

        var forbiddenRead = await accountantController.GetById("task_002");
        Assert.IsType<ForbidResult>(forbiddenRead.Result);

        var forbiddenUpdate = await accountantController.Update("task_002", new TaskItem
        {
            Title = "Nope",
            Status = "done",
            Priority = "high"
        });
        Assert.IsType<ForbidResult>(forbiddenUpdate.Result);

        var forbiddenDelete = await accountantController.Delete("task_002");
        Assert.IsType<ForbidResult>(forbiddenDelete);
    }

    [Fact]
    public async Task MeEndpoint_ReturnsScopedProfileAndPermissions()
    {
        await using var db = BuildDb();
        Seed(db);

        db.Users.AddRange(
            new User
            {
                Id = "u_admin_001",
                FullName = "Admin",
                Email = "admin@test.com",
                PasswordHash = "hash",
                Role = "admin",
                ClientIdsJson = "[]"
            },
            new User
            {
                Id = "u_client_001",
                FullName = "Client User",
                Email = "client@test.com",
                PasswordHash = "hash",
                Role = "client",
                ClientIdsJson = "[\"c_001\"]"
            });
        await db.SaveChangesAsync();

        var controller = new AuthController(db, Microsoft.Extensions.Options.Options.Create(new SecureClientPortal.Backend.Auth.JwtOptions
        {
            SigningKey = "test-signing-key-test-signing-key",
            Audience = "test",
            Issuer = "test"
        }))
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };

        var result = await controller.Me(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.Contains("\"role\":\"client\"", json);
        Assert.Contains("\"clientIds\":[\"c_001\"]", json);
        Assert.Contains("\"permissions\":[\"auth.login\",\"clients.read\",\"assignments.read\"]", json);
    }

    [Fact]
    public async Task AssignmentsEndpoint_RespectsVisibilityAndPrimaryFlag()
    {
        await using var db = BuildDb();
        Seed(db);

        db.Users.AddRange(
            new User
            {
                Id = "u_acc_002",
                FullName = "Backup Accountant",
                Email = "acc2@test.com",
                PasswordHash = "hash",
                Role = "accountant",
                ClientIdsJson = "[]"
            },
            new User
            {
                Id = "u_client_001",
                FullName = "Client User",
                Email = "client@test.com",
                PasswordHash = "hash",
                Role = "client",
                ClientIdsJson = "[\"c_001\"]"
            });
        db.ClientAssignments.Add(new ClientAssignment
        {
            Id = "ca_002",
            AccountantUserId = "u_acc_002",
            ClientId = "c_002",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var accountantController = new AssignmentsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_acc_001", "accountant"))
        };
        var accountantResult = await accountantController.GetAll();
        var accountantOk = Assert.IsType<OkObjectResult>(accountantResult.Result);
        var accountantJson = JsonSerializer.Serialize(accountantOk.Value);
        Assert.Contains("\"clientId\":\"c_001\"", accountantJson);
        Assert.DoesNotContain("\"clientId\":\"c_002\"", accountantJson);
        Assert.Contains("\"isPrimary\":true", accountantJson);

        var clientController = new AssignmentsController(db)
        {
            ControllerContext = BuildControllerContext(BuildUser("u_client_001", "client", ["c_001"]))
        };
        var clientResult = await clientController.GetAll();
        var clientOk = Assert.IsType<OkObjectResult>(clientResult.Result);
        var clientJson = JsonSerializer.Serialize(clientOk.Value);
        Assert.Contains("\"clientId\":\"c_001\"", clientJson);
        Assert.DoesNotContain("\"clientId\":\"c_002\"", clientJson);
    }

    private static async Task<int> GetClientsCount(PortalDbContext db, ClaimsPrincipal user)
    {
        var controller = new ClientsController(db)
        {
            ControllerContext = BuildControllerContext(user)
        };

        var result = await controller.GetAll();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsAssignableFrom<IEnumerable<Client>>(ok.Value);
        return data.Count();
    }

    private static async Task<int> GetDocumentsCount(PortalDbContext db, ClaimsPrincipal user)
    {
        var controller = new DocumentsController(db, new TestFileStorage())
        {
            ControllerContext = BuildControllerContext(user)
        };

        var result = await controller.GetAll();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsAssignableFrom<IEnumerable<Document>>(ok.Value);
        return data.Count();
    }

    private static async Task<int> GetRequestsCount(PortalDbContext db, ClaimsPrincipal user)
    {
        var controller = new RequestsController(db)
        {
            ControllerContext = BuildControllerContext(user)
        };

        var result = await controller.GetAll();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsAssignableFrom<IEnumerable<RequestItem>>(ok.Value);
        return data.Count();
    }

    private static async Task<int> GetTasksCount(PortalDbContext db, ClaimsPrincipal user)
    {
        var controller = new TasksController(db)
        {
            ControllerContext = BuildControllerContext(user)
        };

        var result = await controller.GetAll();
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
            .UseInMemoryDatabase($"scope-test-{Guid.NewGuid():N}")
            .Options;
        return new PortalDbContext(options);
    }

    private static void Seed(PortalDbContext db)
    {
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

        db.Documents.AddRange(
            new Document
            {
                Id = "doc_001",
                ClientId = "c_001",
                Name = "Doc 1",
                Category = "invoices",
                Status = "uploaded",
                SizeBytes = 1,
                UploadedByUserId = "u_client_001"
            },
            new Document
            {
                Id = "doc_002",
                ClientId = "c_002",
                Name = "Doc 2",
                Category = "invoices",
                Status = "uploaded",
                SizeBytes = 1,
                UploadedByUserId = "u_client_002"
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

        db.Tasks.AddRange(
            new TaskItem
            {
                Id = "task_001",
                ClientId = "c_001",
                Title = "Review Alpha",
                Status = "todo",
                Priority = "medium",
                CreatedByUserId = "u_acc_001"
            },
            new TaskItem
            {
                Id = "task_002",
                ClientId = "c_002",
                Title = "Review Beta",
                Status = "todo",
                Priority = "high",
                CreatedByUserId = "u_acc_002"
            });

        db.SaveChanges();
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
}
