using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Tests;

public sealed class AdministratorOwnershipTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PortalDbContext Db() => new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static IConfiguration Config(Guid id) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Administration:OwnerUserId",id.ToString()}}).Build();
    private static User Person(UserRole role = UserRole.Admin, bool active = true)
    {
        var id=Guid.NewGuid(); var user=User.CreateInvited(id,"Isolated",id+"@example.invalid",role,"hash","[]",null);
        if(active)user.CompleteSetup("Isolated","hash"); return user;
    }
    private static CurrentUserContext Actor(User user) => new(user.Id,"admin","admin",RolePermissions.ForRole("admin"),null,null);
    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString()),new Claim("role_scope","admin")],"test"));
    private static async Task Seed(PortalDbContext db,params User[] users)
    {
        db.Users.AddRange(users);
        db.RoleDefinitions.AddRange(RoleDefinition.Create("admin","Admin","admin",RolePermissions.SerializePermissions(RolePermissions.ForRole("admin")),true), RoleDefinition.Create("accountant","Accountant","accountant","[]",true));
        await db.SaveChangesAsync(Ct);
    }
    [Theory]
    [InlineData("create")][InlineData("update")][InlineData("activate")][InlineData("deactivate")]
    public async Task Internal_administrator_cannot_mutate_roles_even_with_all_permission_claims(string action)
    {
        await using var db=Db();var owner=Person();var staff=Person();await Seed(db,owner,staff);
        var service=new RoleService(db,Config(owner.Id));
        await Assert.ThrowsAsync<AppForbiddenException>(async()=> {
            if(action=="create")await service.CreateAsync(new("custom","Custom","accountant",[]),Actor(staff),Ct);
            else if(action=="update")await service.UpdateAsync("accountant",new("Changed","accountant",[]),Actor(staff),Ct);
            else await service.UpdateActivationAsync("accountant",action=="activate",Actor(staff),Ct);
        });
        Assert.Empty(await db.AuditLogs.ToListAsync(Ct)); Assert.Equal("Accountant",(await db.RoleDefinitions.SingleAsync(x=>x.Name=="accountant",Ct)).DisplayName);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Unconfigured_or_inactive_owner_is_denied(bool configured)
    {
        await using var db=Db();var owner=Person(active:false);await Seed(db,owner);
        await Assert.ThrowsAsync<AppForbiddenException>(()=>new RoleService(db,configured?Config(owner.Id):null).UpdateAsync("accountant",new("Changed","accountant",[]),Actor(owner),Ct));
    }
    [Fact] public async Task Current_database_permissions_override_owner_token_claims()
    {
        await using var db=Db();var owner=Person();await Seed(db,owner);
        var role=await db.RoleDefinitions.SingleAsync(x=>x.Name=="admin",Ct);role.UpdateDefinition("Admin","admin",RolePermissions.SerializePermissions(["access.admin"]),true);await db.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<AppForbiddenException>(()=>new RoleService(db,Config(owner.Id)).UpdateAsync("accountant",new("Changed","accountant",[]),Actor(owner),Ct));
    }
    [Theory][InlineData("access.admin")][InlineData("auth.login")][InlineData("users.update")][InlineData("roles.update")]
    public async Task Owner_cannot_remove_core_administrator_access(string removed)
    {
        await using var db=Db();var owner=Person();await Seed(db,owner);
        await Assert.ThrowsAsync<ArgumentException>(()=>new RoleService(db,Config(owner.Id)).UpdateAsync("admin",new("Admin","admin",RolePermissions.ForRole("admin").Where(x=>x!=removed).ToArray()),Actor(owner),Ct));
        Assert.Contains(removed,RolePermissions.ParsePermissions((await db.RoleDefinitions.SingleAsync(x=>x.Name=="admin",Ct)).PermissionsJson));
    }
    [Fact] public async Task Owner_cannot_deactivate_administrator_role()
    {
        await using var db=Db();var owner=Person();await Seed(db,owner);
        await Assert.ThrowsAsync<ArgumentException>(()=>new RoleService(db,Config(owner.Id)).UpdateActivationAsync("admin",false,Actor(owner),Ct));
    }
    [Fact] public async Task Last_admin_protection_applies_to_both_user_apis_and_role_changes()
    {
        await using var db=Db();var admin=Person();var invited=Person(active:false);var disabled=Person();disabled.SetSecurityStatus(SecurityStatus.Disabled);await Seed(db,admin,invited,disabled);
        var service=new AdminService(db,new Mail(),new Links());var users=new UserService(db,new Mail(),new Links());
        Assert.True((await service.UpdateUserStatusAsync(admin.Id.ToString(),new("disabled"),Principal(),Ct)).Forbidden);
        Assert.True((await service.UpdateUserStatusAsync(admin.Id.ToString(),new("locked"),Principal(),Ct)).Forbidden);
        Assert.True((await service.UpdateUserRoleAsync(admin.Id.ToString(),new("accountant"),Principal(),Ct)).Forbidden);
        Assert.True((await users.UpdateActivationAsync(admin.Id.ToString(),new(false,"Security regression test"),Principal(),Ct)).Forbidden);
        Assert.Equal("active",UserSecurityProfile.GetStatus(admin.SecurityJson));Assert.Equal("admin",admin.Role);
    }
    [Fact] public async Task Two_admins_can_reduce_to_one_but_not_zero()
    {
        await using var db=Db();var first=Person();var second=Person();await Seed(db,first,second);var service=new AdminService(db,new Mail(),new Links());
        Assert.Null((await service.UpdateUserStatusAsync(first.Id.ToString(),new("disabled"),Principal(),Ct)).Error);
        Assert.True((await service.UpdateUserStatusAsync(second.Id.ToString(),new("disabled"),Principal(),Ct)).Forbidden);
    }
    [Fact] public async Task Designated_owner_cannot_be_disabled_or_demoted_even_with_another_admin()
    {
        await using var db=Db();var owner=Person();await Seed(db,owner,Person());var config=Config(owner.Id);
        var service=new AdminService(db,new Mail(),new Links(),config);var users=new UserService(db,new Mail(),new Links(),config);
        Assert.True((await service.UpdateUserRoleAsync(owner.Id.ToString(),new("accountant"),Principal(),Ct)).Forbidden);
        Assert.True((await service.UpdateUserStatusAsync(owner.Id.ToString(),new("disabled"),Principal(),Ct)).Forbidden);
        Assert.True((await users.UpdateActivationAsync(owner.Id.ToString(),new(false,"Security regression test"),Principal(),Ct)).Forbidden);
    }
    [Fact] public async Task Owner_restrictions_return_a_clear_http_403()
    {
        var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();context.Response.Body=new MemoryStream();
        var middleware=new SecureClientPortal.Backend.Api.Security.ApiExceptionMiddleware(_=>throw new AppForbiddenException("Owner required."),Microsoft.Extensions.Logging.Abstractions.NullLogger<SecureClientPortal.Backend.Api.Security.ApiExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);Assert.Equal(403,context.Response.StatusCode);
        context.Response.Body.Position=0;Assert.Contains("ACCESS_DENIED",await new StreamReader(context.Response.Body).ReadToEndAsync(Ct));
    }
    [Fact] public async Task Role_edit_capabilities_match_current_owner_access_and_fail_closed()
    {
        await using var db=Db();var owner=Person();var staff=Person();await Seed(db,owner,staff);
        var service=new RoleService(db,Config(owner.Id));
        Assert.Equal(new RoleManagementAccess(false,false,false,false),await service.GetAccessAsync(Actor(staff),Ct));
        Assert.Equal(new RoleManagementAccess(true,true,true,true),await service.GetAccessAsync(Actor(owner),Ct));
        owner.SetSecurityStatus(SecurityStatus.Disabled);await db.SaveChangesAsync(Ct);
        Assert.Equal(new RoleManagementAccess(false,false,false,false),await service.GetAccessAsync(Actor(owner),Ct));
    }
    private sealed class Mail:IAccessEmailSender
    {
        public Task<AccessEmailDispatchResult> SendInviteAsync(string e,string n,string u,DateTime d,CancellationToken ct)=>throw new NotSupportedException();
        public Task<AccessEmailDispatchResult> SendPasswordResetAsync(string e,string n,string u,DateTime d,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Links:IAccessLinkBuilder
    {
        public string BuildSetupUrl(string e,string t)=>throw new NotSupportedException();
        public string BuildPasswordResetUrl(string e,string t)=>throw new NotSupportedException();
    }
}
