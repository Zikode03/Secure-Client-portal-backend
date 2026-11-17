using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Infrastructure.Modules.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application;
using SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Api.Security;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
using SecureClientPortal.Backend.Models;
using Xunit;

namespace SecureClientPortal.Backend.Tests;
public sealed class PermissionRevocationTests
{
    private static ClaimsPrincipal Principal(params string[] permissions) => new(new ClaimsIdentity(
        new[] {new Claim("role_scope","accountant"),new Claim(RolePermissions.AuthoritativeClaimType,"true")}
            .Concat(permissions.Select(p=>new Claim("permission",p))),"test"));
    private static PortalDbContext Db() => new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly CurrentUserContext Admin = new(Guid.NewGuid(),"admin","admin",["access.admin"],null,null);

    private static async Task<RoleService> OwnerService(PortalDbContext db)
    {
        var owner = User.CreateInvited(Admin.UserId!.Value,"Owner","owner@example.invalid",UserRole.Admin,"hash","[]",null);
        owner.CompleteSetup("Owner","hash"); db.Users.Add(owner);
        db.RoleDefinitions.Add(RoleDefinition.Create("admin","Admin","admin",RolePermissions.SerializePermissions(RolePermissions.ForRole("admin")),true));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new RoleService(db,new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Administration:OwnerUserId",owner.Id.ToString()}}).Build());
    }
    [Fact] public void Explicit_permissions_never_fall_back_to_scope_defaults()
    {
        var user=Principal("access.accountant","clients.read");
        Assert.True(user.HasPermission("clients.read"));
        Assert.False(user.HasPermission("clients.create"));
        Assert.False(user.HasPermission("documents.review"));
        Assert.False(Principal("access.admin").HasPermission("users.create"));
    }
    [Fact] public void Empty_authoritative_permissions_stay_empty_in_every_context()
    {
        var user=Principal();
        Assert.False(user.HasPermission("access.accountant"));
        Assert.Empty(new CurrentUserContextFactory().Create(user).Permissions);
    }
    [Fact] public void Operation_filter_denies_read_only_staff_and_allows_explicit_reviewers()
    {
        AuthorizationFilterContext Context(ClaimsPrincipal user) => new(new ActionContext(new DefaultHttpContext {User=user},new RouteData(),new ActionDescriptor()),new List<IFilterMetadata>());
        var filter=new RequirePermissionAttribute("documents.review");
        var denied=Context(Principal("access.accountant","documents.read")); filter.OnAuthorization(denied);
        Assert.IsType<ForbidResult>(denied.Result);
        var allowed=Context(Principal("access.accountant","documents.review")); filter.OnAuthorization(allowed); Assert.Null(allowed.Result);
        var anonymous=Context(new ClaimsPrincipal(new ClaimsIdentity())); filter.OnAuthorization(anonymous); Assert.IsType<UnauthorizedResult>(anonymous.Result);
    }
    [Fact] public void Current_claims_remove_old_permissions_and_membership_claims()
    {
        var user=User.CreateInvited(Guid.NewGuid(),"Test","test@example.invalid",UserRole.Accountant,"hash","[]",null);
        var role=RoleDefinition.Create("accountant","Accountant","accountant","[]",true);
        var principal=Principal("access.accountant","clients.create");
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("client_id",Guid.NewGuid().ToString()));
        CurrentAuthorizationClaims.Replace(principal,user,role,["access.accountant","clients.read"]);
        Assert.False(principal.HasPermission("clients.create")); Assert.True(principal.HasPermission("clients.read")); Assert.Empty(principal.FindAll("client_id"));
        CurrentAuthorizationClaims.Replace(principal,user,role,[]);
        Assert.False(principal.HasPermission("access.accountant"));
    }
    [Fact] public async Task Explicit_admin_role_permissions_do_not_expand_from_the_catalog()
    {
        await using var db=Db();
        db.Permissions.Add(Permission.Create("users.create","Create users","",false,true)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var role=RoleDefinition.Create("admin","Admin","admin","[\"access.admin\"]",true);
        Assert.Equal(["access.admin"],await PermissionResolution.ResolvePermissionsAsync(db,role,"admin",TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editing_or_disabling_a_role_revokes_all_its_sessions_only(bool disable)
    {
        await using var db=Db();
        var role=RoleDefinition.Create("accountant","Accountant","accountant",RolePermissions.SerializePermissions(RolePermissions.ForRole("accountant")),true);
        var staff=User.CreateInvited(Guid.NewGuid(),"Staff","staff@example.invalid",UserRole.Accountant,"hash","[]",null);
        var other=User.CreateInvited(Guid.NewGuid(),"Client","client@example.invalid",UserRole.Client,"hash","[]",null);
        db.RoleDefinitions.Add(role); db.Users.AddRange(staff,other);
        var first=UserSession.Start(Guid.NewGuid(),staff.Id,Guid.NewGuid(),DateTime.UtcNow.AddHours(1),null,null);
        var second=UserSession.Start(Guid.NewGuid(),staff.Id,Guid.NewGuid(),DateTime.UtcNow.AddHours(2),null,null);
        var unaffected=UserSession.Start(Guid.NewGuid(),other.Id,Guid.NewGuid(),DateTime.UtcNow.AddHours(1),null,null);
        db.UserSessions.AddRange(first,second,unaffected); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service=await OwnerService(db);
        if(disable) await service.UpdateActivationAsync("accountant",false,Admin,TestContext.Current.CancellationToken);
        else await service.UpdateAsync("accountant",new UpdateRoleRequest("Accountant","accountant",["access.accountant","clients.read"]),Admin,TestContext.Current.CancellationToken);
        Assert.NotNull(first.RevokedAtUtc); Assert.NotNull(second.RevokedAtUtc); Assert.Null(unaffected.RevokedAtUtc);
        Assert.Equal(disable?"role_deactivated":"role_permissions_changed",first.RevokedReason);
    }
    [Fact] public async Task Saving_an_empty_role_permission_list_does_not_restore_defaults()
    {
        await using var db=Db(); db.RoleDefinitions.Add(RoleDefinition.Create("accountant","Accountant","accountant","[\"access.accountant\"]",true));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await (await OwnerService(db)).UpdateAsync("accountant",new UpdateRoleRequest("Accountant","accountant",[]),Admin,TestContext.Current.CancellationToken);
        Assert.Empty(RolePermissions.ParsePermissions((await db.RoleDefinitions.SingleAsync(x=>x.Name=="accountant",TestContext.Current.CancellationToken)).PermissionsJson));
    }
    [Fact] public async Task Read_only_staff_cannot_create_clients_even_when_calling_the_service_directly()
    {
        await using var db=Db();
        var request=Client.Create(Guid.NewGuid(),"Test","Pty Ltd","Contact","contact@example.invalid",ClientStatus.Active);
        var result=await new ClientService(db).CreateAsync(request,Principal("access.accountant","clients.read"),TestContext.Current.CancellationToken);
        Assert.True(result.forbidden); Assert.Null(result.created); Assert.Empty(await db.Clients.ToListAsync(TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData(typeof(SecureClientPortal.Backend.Api.Modules.Documents.DocumentsController),"Review","documents.review")]
    [InlineData(typeof(SecureClientPortal.Backend.Api.Modules.Documents.InvoiceReviewController),"Save","documents.review")]
    [InlineData(typeof(SecureClientPortal.Backend.Api.Modules.ReviewQueue.ReviewQueueController),"Review","documents.review")]
    [InlineData(typeof(SecureClientPortal.Backend.Api.Modules.MonthlyPacks.DocumentSlotsController),"Approve","documents.review")]
    public void Every_document_review_entry_point_requires_the_review_permission(Type controller,string method,string permission)
    {
        var attribute=Assert.Single(controller.GetMethod(method)!.GetCustomAttributes(typeof(RequirePermissionAttribute),true).Cast<RequirePermissionAttribute>());
        Assert.Equal(permission,attribute.Permission);
    }
    [Fact] public async Task Startup_upgrades_only_untouched_defaults_and_preserves_custom_restrictions()
    {
        await using var db=Db();
        var legacy=RolePermissions.ForRole("accountant").Except(RolePermissions.AddedOperationPermissions("accountant"));
        db.RoleDefinitions.Add(RoleDefinition.Create("accountant","Accountant","accountant",RolePermissions.SerializePermissions(legacy),true));
        db.RoleDefinitions.Add(RoleDefinition.Create("admin","Restricted admin","admin","[\"access.admin\",\"roles.read\"]",true));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var services=new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        await SeedData.InitializeAsync(services);
        var staff=await db.RoleDefinitions.SingleAsync(x=>x.Name=="accountant",TestContext.Current.CancellationToken);
        Assert.Contains("documents.review",RolePermissions.ParsePermissions(staff.PermissionsJson));
        var admin=await db.RoleDefinitions.SingleAsync(x=>x.Name=="admin",TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access.admin","roles.read"},RolePermissions.ParsePermissions(admin.PermissionsJson));
    }

}
