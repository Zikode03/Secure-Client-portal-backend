using System.Data;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Clients;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Application.Modules.Clients;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Clients;

public sealed class ClientOnboardingService(PortalDbContext db, IAccessEmailSender emailSender, IAccessLinkBuilder links) : IClientOnboardingService
{
    private sealed record Receipt(string Fingerprint, ClientOnboardingResponse Response);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static bool Available(User u) => UserSecurityProfile.GetStatus(u.SecurityJson) is "active" or "invited";
    private static ServiceResult<T> Invalid<T>(string message, int status = 400) => ServiceResult<T>.ErrorResult(message, statusCode: status);

    public async Task<ServiceResult<ClientOnboardingOptions>> GetOptionsAsync(ClaimsPrincipal actor, CancellationToken ct)
    {
        if (!actor.IsAdmin()) return ServiceResult<ClientOnboardingOptions>.ForbiddenResult();
        var roles = await db.RoleDefinitions.Where(x => x.IsActive).ToListAsync(ct);
        var people = (await db.Users.OrderBy(x => x.FullName).ToListAsync(ct)).Where(Available).ToArray();
        OnboardingPerson[] ForScope(string scope) => people.Where(p => roles.Any(r => r.Name == p.Role && r.Scope == scope))
            .Select(p => new OnboardingPerson(p.Id, p.FullName, p.Email)).ToArray();
        return ServiceResult<ClientOnboardingOptions>.Success(new(ForScope("accountant"), ForScope("client")));
    }

    public async Task<ServiceResult<ClientOnboardingResponse>> CreateAsync(OnboardClientRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (!actor.IsAdmin()) return ServiceResult<ClientOnboardingResponse>.ForbiddenResult();
        if (request.RequestId == Guid.Empty || request.AccountantUserId == Guid.Empty
            || !Text(request.Name, 200) || !Text(request.EntityType, 100) || !Text(request.Industry, 100)
            || !Text(request.ContactName, 200) || !Text(request.ContactEmail, 320) || request.RegistrationNumber?.Length > 100)
            return Invalid<ClientOnboardingResponse>("Business name, entity type, industry, contact details and an accountant are required.");
        var email = request.ContactEmail.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            return Invalid<ClientOnboardingResponse>("Enter a valid contact email address.");
        request = request with { Name = request.Name.Trim(), EntityType = request.EntityType.Trim(), Industry = request.Industry.Trim(),
            ContactName = request.ContactName.Trim(), ContactEmail = email, RegistrationNumber = request.RegistrationNumber?.Trim() };
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json))));
        var receiptKey = "client-onboarding:" + request.RequestId;

        // One transaction for the business, access, assignment, audit and retry receipt.
        // Serializable protects existing-user read/modify/write links from lost updates.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var prior = await db.SystemSettings.FindAsync([receiptKey], ct);
        if (prior is not null)
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(prior.ValueJson, Json)!;
            return receipt.Fingerprint == fingerprint
                ? ServiceResult<ClientOnboardingResponse>.Success(receipt.Response)
                : Invalid<ClientOnboardingResponse>("This onboarding request was already used with different details.", 409);
        }
        if (!string.IsNullOrEmpty(request.RegistrationNumber) &&
            await db.Clients.AnyAsync(x => x.RegistrationNumber == request.RegistrationNumber, ct))
            return Invalid<ClientOnboardingResponse>("A business with this registration number already exists.", 409);

        var roles = await db.RoleDefinitions.Where(x => x.IsActive).ToListAsync(ct);
        var accountant = await db.Users.FindAsync([request.AccountantUserId], ct);
        if (accountant is null || !Available(accountant) || !roles.Any(r => r.Name == accountant.Role && r.Scope == "accountant"))
            return Invalid<ClientOnboardingResponse>("Select an available accountant.");
        User? user = null;
        var createdUser = !request.ExistingClientUserId.HasValue;
        if (!createdUser)
        {
            user = await db.Users.FindAsync([request.ExistingClientUserId!.Value], ct);
            if (user is null || !Available(user) || !roles.Any(r => r.Name == user.Role && r.Scope == "client"))
                return Invalid<ClientOnboardingResponse>("Select an available client user, not a staff account.");
        }
        else
        {
            if (!roles.Any(r => r.Name == "client" && r.Scope == "client"))
                return Invalid<ClientOnboardingResponse>("The client role must be enabled before inviting a client.");
            if (await db.Users.AnyAsync(x => x.Email == email, ct))
                return Invalid<ClientOnboardingResponse>("This email already has an account. Select its existing client user instead.", 409);
        }

        var clientId = Guid.NewGuid();
        string? setupUrl = null;
        var expires = DateTime.UtcNow.AddHours(24);
        if (createdUser)
        {
            user = User.CreateInvited(Guid.NewGuid(), request.ContactName, email, UserRole.Client,
                PasswordHasher.Hash(AccessTokenCodec.GenerateToken()), JsonSerializer.Serialize(new[] { clientId }),
                JsonSerializer.Serialize(new { company = request.Name }));
            var token = AccessTokenCodec.GenerateToken();
            setupUrl = links.BuildSetupUrl(email, token);
            db.Users.Add(user);
            db.UserAccessTokens.Add(UserAccessToken.Create(Guid.NewGuid(), user.Id, "invite",
                AccessTokenCodec.HashToken(token), expires, null, actor.GetUserId()));
        }
        else
        {
            Guid[] current;
            try { current = JsonSerializer.Deserialize<Guid[]>(user!.ClientIdsJson) ?? []; }
            catch (JsonException) { return Invalid<ClientOnboardingResponse>("Existing business links need repair before adding access.", 409); }
            user!.SetClientIdsJson(JsonSerializer.Serialize(current.Append(clientId).Distinct()));
            // Access is encoded in sessions; require a fresh sign-in for the new business.
            foreach (var session in await db.UserSessions.Where(x => x.UserId == user.Id && x.RevokedAtUtc == null).ToListAsync(ct))
                session.Revoke("Client business access updated");
        }
        var client = Client.Create(clientId, request.Name, request.EntityType, request.ContactName, email, ClientStatus.Active);
        client.AssignAccountant(accountant.Id);
        client.UpdateBusinessProfile(request.Name, "", request.RegistrationNumber ?? "", "", "", request.ContactName,
            email, "", "", "", "South Africa", request.Industry, "");
        db.Clients.Add(client);
        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), accountant.Id, clientId));
        var result = new ClientOnboardingResponse(clientId, client.Name, user!.Id, accountant.Id, createdUser,
            createdUser ? "pending" : "not_required",
            createdUser ? "Business and access saved. Invitation delivery has not yet been confirmed."
                : "Business linked to the existing client user. They must sign in again to refresh their access.");
        var savedReceipt = SystemSetting.Create(receiptKey, JsonSerializer.Serialize(new Receipt(fingerprint, result), Json));
        db.SystemSettings.Add(savedReceipt);
        db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(), actor.GetUserId(), "admin", "clients.onboarded", "client", clientId, clientId,
            JsonSerializer.Serialize(new { userId = user.Id, accountantId = accountant.Id, createdUser })));
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        { return Invalid<ClientOnboardingResponse>("A matching account or onboarding request was saved concurrently. Retry to recover the result.", 409); }
        // The transaction is committed before contacting SMTP; an email outage cannot
        // leave a half-created business or cause retries to create a second business.
        if (createdUser)
        {
            string delivery;
            try
            {
                var dispatch = await emailSender.SendInviteAsync(user.Email, user.FullName, setupUrl!, expires, ct);
                delivery = dispatch.DeliveryMode;
            }
            catch (Exception) when (!ct.IsCancellationRequested) { delivery = "failed"; }
            result = result with { InvitationDelivery = delivery,
                Message = delivery == "smtp" ? "Business created, client linked and invitation sent."
                    : "Business and client access were saved, but email delivery was not confirmed. Check SMTP, then use Users > Reset password to issue a fresh setup email; do not create the business again." };
            savedReceipt.UpdateValue(JsonSerializer.Serialize(new Receipt(fingerprint, result), Json));
            db.AuditLogs.Add(AuditLog.Create(Guid.NewGuid(), actor.GetUserId(), "admin", "clients.invitation_delivery",
                "client", clientId, clientId, JsonSerializer.Serialize(new { userId = user.Id, delivery })));
            await db.SaveChangesAsync(ct);
        }
        return ServiceResult<ClientOnboardingResponse>.Success(result);
    }
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
}
