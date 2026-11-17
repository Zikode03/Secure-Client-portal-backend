using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
using SecureClientPortal.Backend.Infrastructure.Modules.Platform;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
namespace SecureClientPortal.Backend.Tests;
public sealed class AdminControlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PortalDbContext Db() => new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ClaimsPrincipal Actor(params string[] operations) => new(new ClaimsIdentity(new[] {new Claim(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString()),new Claim("role_scope","admin"),new Claim("permission","access.admin"),new Claim(RolePermissions.AuthoritativeClaimType,"true")}.Concat(operations.Select(x=>new Claim("permission",x))),"test"));
    private static User Person(UserRole role, bool active=true)
    { var id=Guid.NewGuid();var u=User.CreateInvited(id,"Isolated",id+"@example.invalid",role,"unchanged-hash","[]",null);if(active)u.CompleteSetup("Isolated","unchanged-hash");return u; }
    private static AdminService Service(PortalDbContext db, Mail? mail=null)=>new(db,mail??new Mail(),new Links());
    [Fact] public void Administrator_identity_does_not_grant_accountant_identity_or_review()
    {
        var operations=RolePermissions.ForRole("admin");
        Assert.DoesNotContain("documents.review",operations);Assert.DoesNotContain("access.accountant",operations);Assert.DoesNotContain("access.client",operations);
        Assert.Contains("documents.review",RolePermissions.ForRole("accountant"));
        Assert.True(Actor("documents.review").HasPermission("documents.review"));Assert.False(Actor().HasPermission("documents.review"));
    }
    [Fact] public async Task Untouched_old_admin_default_is_narrowed_and_its_sessions_revoked()
    {
        await using var db=Db();var user=Person(UserRole.Admin);db.Users.Add(user);
        db.RoleDefinitions.Add(RoleDefinition.Create("admin","Admin","admin",RolePermissions.SerializePermissions(RolePermissions.ForRole("admin").Concat(new[]{"access.accountant","access.client","documents.review"})),true));
        var session=UserSession.Start(Guid.NewGuid(),user.Id,Guid.NewGuid(),DateTime.UtcNow.AddHours(1),null,null);db.UserSessions.Add(session);await db.SaveChangesAsync(Ct);
        using var services=new ServiceCollection().AddSingleton(db).BuildServiceProvider();await SeedData.InitializeAsync(services);
        Assert.DoesNotContain("documents.review",RolePermissions.ParsePermissions((await db.RoleDefinitions.SingleAsync(x=>x.Name=="admin",Ct)).PermissionsJson));Assert.NotNull(session.RevokedAtUtc);
    }
    [Fact] public async Task Resending_invitation_rotates_setup_token_without_resetting_account()
    {
        await using var db=Db();var u=Person(UserRole.Accountant,false);db.Users.Add(u);
        var old=UserAccessToken.Create(Guid.NewGuid(),u.Id,"invite","old-hash",DateTime.UtcNow.AddHours(1),createdAtUtc:DateTime.UtcNow.AddMinutes(-2));db.UserAccessTokens.Add(old);await db.SaveChangesAsync(Ct);
        var mail=new Mail();var result=await Service(db,mail).ResendInvitationAsync(u.Id.ToString(),Actor("users.create"),Ct);
        Assert.Null(result.Error);Assert.NotNull(old.ConsumedAtUtc);Assert.Equal("invited",UserSecurityProfile.GetStatus(u.SecurityJson));Assert.Equal("unchanged-hash",u.PasswordHash);Assert.Single(mail.Invites);
        var audit=await db.AuditLogs.SingleAsync(Ct);Assert.Equal("users.invitation_resent",audit.Action);Assert.DoesNotContain("token=",audit.MetadataJson!);
        Assert.Equal(429,(await Service(db,mail).ResendInvitationAsync(u.Id.ToString(),Actor("users.create"),Ct)).StatusCode);
    }
    [Fact] public async Task Resend_cannot_be_used_on_active_users_or_without_permission()
    {
        await using var db=Db();var u=Person(UserRole.Client);db.Users.Add(u);await db.SaveChangesAsync(Ct);
        Assert.True((await Service(db).ResendInvitationAsync(u.Id.ToString(),Actor(),Ct)).Forbidden);
        Assert.Equal(409,(await Service(db).ResendInvitationAsync(u.Id.ToString(),Actor("users.create"),Ct)).StatusCode);Assert.Empty(db.UserAccessTokens);
    }
    [Fact] public async Task Session_revocation_does_not_reset_password_or_disable_user()
    {
        await using var db=Db();var u=Person(UserRole.Client);db.Users.Add(u);var session=UserSession.Start(Guid.NewGuid(),u.Id,Guid.NewGuid(),DateTime.UtcNow.AddHours(1),null,null);db.UserSessions.Add(session);var token=UserAccessToken.Create(Guid.NewGuid(),u.Id,"refresh","hash",DateTime.UtcNow.AddDays(1),session.Id);db.UserAccessTokens.Add(token);await db.SaveChangesAsync(Ct);
        Assert.Null((await Service(db).RevokeSessionsAsync(u.Id.ToString(),Actor("users.update"),Ct)).Error);
        Assert.NotNull(session.RevokedAtUtc);Assert.NotNull(token.ConsumedAtUtc);Assert.Equal("active",UserSecurityProfile.GetStatus(u.SecurityJson));Assert.Equal("unchanged-hash",u.PasswordHash);
    }
    [Fact] public async Task Last_active_administrator_cannot_be_disabled_or_demoted()
    {
        await using var db=Db();var u=Person(UserRole.Admin);db.Users.Add(u);db.RoleDefinitions.Add(RoleDefinition.Create("accountant","Accountant","accountant","[]",true));await db.SaveChangesAsync(Ct);
        Assert.True((await Service(db).UpdateUserStatusAsync(u.Id.ToString(),new("disabled"),Actor("users.update"),Ct)).Forbidden);
        Assert.True((await Service(db).UpdateUserRoleAsync(u.Id.ToString(),new("accountant"),Actor("users.update"),Ct)).Forbidden);
        var other=Person(UserRole.Admin);db.RoleDefinitions.Add(RoleDefinition.Create("admin","Admin","admin",RolePermissions.SerializePermissions(RolePermissions.ForRole("admin")),true));db.Users.Add(other);await db.SaveChangesAsync(Ct);
        Assert.Null((await Service(db).UpdateUserStatusAsync(u.Id.ToString(),new("disabled"),Actor("users.update"),Ct)).Error);
    }
    [Fact] public async Task Setting_changes_are_audited_without_values_and_operational_results_are_read_only()
    {
        await using var db=Db();await Service(db).PutSettingAsync("firm.profile",new("{\"firmName\":\"Private firm\"}"),Actor("settings.update"),Ct);
        var audit=await db.AuditLogs.SingleAsync(Ct);Assert.Equal("settings.updated",audit.Action);Assert.Contains("firm.profile",audit.MetadataJson!);Assert.DoesNotContain("Private firm",audit.MetadataJson!);
        await Assert.ThrowsAsync<AppValidationException>(()=>Service(db).PutSettingAsync("operations.backup",new("{}"),Actor("settings.update"),Ct));
    }
    [Theory][InlineData("https://tracker.invalid/logo.png")][InlineData("data:image/svg+xml;base64,PHN2Zy8+")][InlineData("data:image/png;base64,YmFk")]
    public void Logos_cannot_be_tracking_urls_svg_or_disguised_files(string logo)=>Assert.Throws<AppValidationException>(()=>FirmBranding.Validate(JsonSerializer.Serialize(new{firmName="Firm",logoDataUrl=logo})));
    [Fact] public async Task Operations_report_live_counts_and_do_not_invent_recovery_success()
    {
        await using var db=Db();var client=Guid.NewGuid();var pack=MonthlyPack.Create(Guid.NewGuid(),client,2026,11);db.MonthlyPacks.Add(pack);db.DocumentSlots.Add(DocumentSlot.Create(Guid.NewGuid(),pack.Id,client,"invoices","Invoices",true,DateTime.UtcNow.AddDays(-1)));db.NotificationEmailDeliveries.Add(new(){NotificationId=Guid.NewGuid(),Status="failed"});await db.SaveChangesAsync(Ct);
        var root=Path.Combine(Path.GetTempPath(),"ops-test-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try { await File.WriteAllTextAsync(Path.Combine(root,"owned.txt"),"123",Ct);var report=await new AdminOperationsService(db,Options.Create(new StorageOptions{RootPath=root}),Options.Create(new AutomationOptions()),new ConfigurationBuilder().Build()).GetAsync(Ct);Assert.Equal(1,report.MissingDocuments);Assert.Equal(1,report.OverdueDocuments);Assert.Equal(1,report.EmailFailed);Assert.Equal(3,report.StorageBytes);Assert.Null(report.LastBackup);Assert.Null(report.LastRestore);Assert.Equal("single_firm",report.DeploymentMode); }
        finally { Directory.Delete(root,true); }
    }
    [Fact] public async Task Saved_rule_edits_and_deliberate_removal_survive_startup()
    {
        await using var db=Db();using var services=new ServiceCollection().AddSingleton(db).BuildServiceProvider();await SeedData.InitializeAsync(services);
        var service=new SecureClientPortal.Backend.Infrastructure.Modules.FirmManagement.Application.FirmManagementService(db);
        var actor=new SecureClientPortal.Backend.Application.CurrentUserContext(Guid.NewGuid(),"admin","admin",RolePermissions.ForRole("admin"),null,null);
        var rules=(await service.GetReminderRulesAsync(Ct)).ToArray();var desired=rules.Select((rule,index)=>index==0?rule with { MessageTemplate="Changed {item} before {dueDate}." }:rule).ToArray();
        await service.ReplaceReminderRulesAsync(desired,actor,Ct);await SeedData.InitializeAsync(services);
        Assert.Equal("Changed {item} before {dueDate}.",(await db.ReminderRules.FindAsync([rules[0].Id],Ct))!.MessageTemplate);
        await service.ReplaceReminderRulesAsync([],actor,Ct);await SeedData.InitializeAsync(services);
        Assert.Empty(await service.GetReminderRulesAsync(Ct));Assert.Empty(await db.ReminderRules.ToListAsync(Ct));
    }
    [Fact] public async Task Saving_existing_template_ids_updates_in_place_without_tracking_conflicts()
    {
        await using var db=Db();using var services=new ServiceCollection().AddSingleton(db).BuildServiceProvider();await SeedData.InitializeAsync(services);
        var service=new SecureClientPortal.Backend.Infrastructure.Modules.FirmManagement.Application.FirmManagementService(db);
        var actor=new SecureClientPortal.Backend.Application.CurrentUserContext(Guid.NewGuid(),"admin","admin",RolePermissions.ForRole("admin"),null,null);
        await service.ReplaceRequiredDocumentTemplatesAsync(await service.GetRequiredDocumentTemplatesAsync(Ct),actor,Ct);
        await service.ReplaceMonthlyPackTemplatesAsync(await service.GetMonthlyPackTemplatesAsync(Ct),actor,Ct);
        await service.ReplaceRequestTemplatesAsync(await service.GetRequestTemplatesAsync(Ct),actor,Ct);
        await service.ReplaceDeadlineRulesAsync(await service.GetDeadlineRulesAsync(Ct),actor,Ct);
        Assert.Equal(4,await db.AuditLogs.CountAsync(Ct));
    }
    private sealed class Mail:IAccessEmailSender
    {
        public List<string> Invites {get;}=[];
        public Task<AccessEmailDispatchResult> SendInviteAsync(string email,string name,string url,DateTime expiry,CancellationToken ct){Invites.Add(email);return Task.FromResult(new AccessEmailDispatchResult("smtp"));}
        public Task<AccessEmailDispatchResult> SendPasswordResetAsync(string email,string name,string url,DateTime expiry,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Links:IAccessLinkBuilder
    {
        public string BuildSetupUrl(string email,string token)=>"https://portal.example.test/setup?token="+token;
        public string BuildPasswordResetUrl(string email,string token)=>throw new NotSupportedException();
    }
}
