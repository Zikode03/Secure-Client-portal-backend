using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using System.Text.Json;

namespace SecureClientPortal.Backend.Controllers;

public record CreateUserRequest(
    string FullName,
    string Email,
    string Role,
    string? Password,
    string[]? ClientIds,
    string? Company);

[ApiController]
[Route("api/users")]
[Authorize(Policy = "AdminOnly")]
public class UsersController : ControllerBase
{
    private static readonly HashSet<string> AllowedRoles = ["admin", "accountant", "client"];
    private readonly PortalDbContext _db;

    public UsersController(PortalDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var users = await _db.Users
            .OrderBy(x => x.FullName)
            .ToListAsync();

        var payload = users.Select(user =>
        {
            var clientIds = ParseClientIds(user.ClientIdsJson);
            return new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.Role,
                clientIds,
                permissions = RolePermissions.ForRole(user.Role),
                user.ProfileJson,
                user.SecurityJson,
                user.CreatedAtUtc,
                user.UpdatedAtUtc
            };
        });

        return Ok(payload);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var normalizedRole = request.Role.Trim().ToLowerInvariant();
        if (!AllowedRoles.Contains(normalizedRole))
        {
            return BadRequest(new { error = "Role must be admin, accountant, or client." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(x => x.Email == email))
        {
            return Conflict(new { error = "A user with this email already exists." });
        }

        var clientIds = normalizedRole == "client"
            ? (request.ClientIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

        if (normalizedRole == "client" && clientIds.Length == 0)
        {
            return BadRequest(new { error = "Client users must be linked to at least one client." });
        }

        if (clientIds.Length > 0)
        {
            var knownClientIds = await _db.Clients
                .Where(x => clientIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync();
            if (knownClientIds.Count != clientIds.Length)
            {
                return BadRequest(new { error = "One or more client ids are invalid." });
            }
        }

        var user = new User
        {
            Id = $"u_{Guid.NewGuid():N}",
            FullName = request.FullName.Trim(),
            Email = email,
            Role = normalizedRole,
            PasswordHash = PasswordHasher.Hash(string.IsNullOrWhiteSpace(request.Password) ? "ChangeMe123!" : request.Password),
            ClientIdsJson = JsonSerializer.Serialize(clientIds),
            ProfileJson = string.IsNullOrWhiteSpace(request.Company)
                ? null
                : JsonSerializer.Serialize(new { company = request.Company.Trim() }),
            SecurityJson = JsonSerializer.Serialize(new { status = "invited" }),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _db.WriteAuditLogAsync(
            User,
            "users.created",
            "user",
            user.Id,
            clientIds.FirstOrDefault(),
            JsonSerializer.Serialize(new { user.Email, user.Role, clientIds }));

        return Created($"/api/users/{user.Id}", new
        {
            user.Id,
            user.FullName,
            user.Email,
            user.Role,
            clientIds,
            permissions = RolePermissions.ForRole(user.Role)
        });
    }

    private static string[] ParseClientIds(string rawJson)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(rawJson)?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
