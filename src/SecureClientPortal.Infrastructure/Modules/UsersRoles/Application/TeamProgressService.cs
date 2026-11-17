using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
namespace SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;

public sealed class TeamProgressService(PortalDbContext db)
{
    public async Task<TeamProgressResponse> GetAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var roles = await db.RoleDefinitions.AsNoTracking().Where(x => x.Scope == "accountant").Select(x => x.Name).ToListAsync(ct);
        if (!roles.Contains("accountant")) roles.Add("accountant");
        var staff = await db.Users.AsNoTracking().Where(x => roles.Contains(x.Role)).OrderBy(x => x.FullName).ToListAsync(ct);
        var staffIds = staff.Select(x => x.Id).ToArray();
        var clients = await db.Clients.AsNoTracking().ToListAsync(ct);
        var clientNames = clients.ToDictionary(x => x.Id, x => x.Name);
        var assignments = await db.ClientAssignments.AsNoTracking().Where(x => staffIds.Contains(x.AccountantUserId)).ToListAsync(ct);
        var assignedClientIds = assignments.Select(x => x.ClientId).Concat(clients.Where(x => staffIds.Contains(x.AssignedAccountantId)).Select(x => x.Id)).Distinct().ToArray();
        var packs = await db.MonthlyPacks.AsNoTracking().Where(x => assignedClientIds.Contains(x.ClientId)).OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync(ct);
        var packIds = packs.Select(x => x.Id).ToArray();
        var slots = await db.DocumentSlots.AsNoTracking().Where(x => packIds.Contains(x.MonthlyPackId)).ToListAsync(ct);
        var documents = await db.Documents.AsNoTracking().Where(x => assignedClientIds.Contains(x.ClientId)).Select(x => new { x.Id, x.Name, x.MonthlyPackId, x.DocumentSlotId, x.Status }).ToListAsync(ct);
        var documentNames = documents.ToDictionary(x => x.Id, x => x.Name);
        var latest = await db.AuditLogs.AsNoTracking().Where(x => x.ActorUserId.HasValue && staffIds.Contains(x.ActorUserId.Value) &&
            (x.Action.StartsWith("documents.") || x.Action.StartsWith("review_queue.") || x.Action.StartsWith("monthly_packs.") || x.Action.StartsWith("requests.") || x.Action.StartsWith("compliance.")))
            .GroupBy(x => x.ActorUserId).Select(g => g.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).First()).ToListAsync(ct);
        var today = now.AddHours(2).Date.AddHours(-2); // South African business day.
        var decisions = await db.ReviewDecisions.AsNoTracking().Where(x => staffIds.Contains(x.ReviewerUserId) && x.DecidedAtUtc >= today && x.DecidedAtUtc <= now &&
            (x.Decision == "accepted" || x.Decision == "rejected" || x.Decision == "request_reupload"))
            .GroupBy(x => x.ReviewerUserId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var slotsByPack = slots.ToLookup(x => x.MonthlyPackId);
        var documentsByPack = documents.Where(x => x.DocumentSlotId == null).ToLookup(x => x.MonthlyPackId);
        var packsByClient = packs.ToLookup(x => x.ClientId);
        var accounts = new List<AccountantProgress>();
        foreach (var person in staff)
        {
            var linkedIds = assignments.Where(x => x.AccountantUserId == person.Id).Select(x => x.ClientId).Concat(clients.Where(x => x.AssignedAccountantId == person.Id).Select(x => x.Id)).Distinct().ToHashSet();
            var work = new List<ClientWorkProgress>();
            foreach (var client in clients.Where(x => linkedIds.Contains(x.Id)).OrderBy(x => x.Name))
            {
                var clientPacks = packsByClient[client.Id].ToArray();
                var open = clientPacks.Where(x => x.Status != "complete" && x.Status != "closed").ToArray();
                var visible = open.Length > 0 ? open : clientPacks.Take(1).ToArray();
                var progress = visible.Select(pack => {
                    var checklist = slotsByPack[pack.Id].ToArray();
                    var required = checklist.Where(x => x.IsRequired && x.Status != "not_applicable").ToArray();
                    var supporting = documentsByPack[pack.Id].Count(x => x.Status == "under_review");
                    return new PackProgress(pack.Id, pack.Year, pack.Month, pack.Status, required.Length, required.Count(x => x.Status == "accepted"),
                        checklist.Count(x => x.Status == "submitted"), checklist.Count(x => x.Status == "under_review") + supporting,
                        required.Count(x => x.Status is "not_started" or "draft" or "rejected" or "reupload_required"));
                }).ToArray();
                work.Add(new(client.Id, client.Name, client.AssignedAccountantId == person.Id ? "primary" : "backup", progress));
            }
            // Primary ownership drives totals; backup coverage is listed without double-crediting the same work.
            var primary = work.Where(x => x.Assignment == "primary").ToArray();
            var primaryPacks = primary.SelectMany(x => x.Packs).ToArray();
            var activity = latest.FirstOrDefault(x => x.ActorUserId == person.Id);
            accounts.Add(new(person.Id, person.FullName, UserSecurityProfile.GetStatus(person.SecurityJson), primary.Length, work.Count - primary.Length,
                primaryPacks.Sum(x => x.AwaitingReview), primaryPacks.Sum(x => x.InReview), primaryPacks.Sum(x => x.RequiredItems), primaryPacks.Sum(x => x.ApprovedItems), primaryPacks.Sum(x => x.WaitingOnClient), decisions.GetValueOrDefault(person.Id),
                activity is null ? null : new(activity.Action, activity.CreatedAtUtc, activity.ClientId.HasValue ? clientNames.GetValueOrDefault(activity.ClientId.Value) : null, documentNames.GetValueOrDefault(activity.EntityId)), work));
        }
        return new(now, clients.Count(x => x.Status == "active" && x.AssignedAccountantId == Guid.Empty), accounts);
    }
}
