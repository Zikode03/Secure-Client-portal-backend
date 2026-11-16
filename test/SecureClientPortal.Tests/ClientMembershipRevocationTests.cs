using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using Xunit;

namespace SecureClientPortal.Backend.Tests;
public sealed class ClientMembershipRevocationTests
{
    [Fact] public async Task Current_database_membership_wins_over_old_token_claims()
    {
        await using var db=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var oldClient=Guid.NewGuid(); var currentClient=Guid.NewGuid();
        var user=User.CreateInvited(Guid.NewGuid(),"Test","test@example.invalid",UserRole.Client,"hash",System.Text.Json.JsonSerializer.Serialize(new[]{currentClient.ToString()}),null);
        db.Users.Add(user); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var actor=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim("role_scope","client"),new Claim("permission","access.client"),new Claim("client_id",oldClient.ToString())},"test"));
        Assert.Equal(new[]{currentClient},(await actor.GetAccessibleClientIdsAsync(db,TestContext.Current.CancellationToken)).ToArray());
        user.SetClientIdsJson("[]"); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await actor.GetAccessibleClientIdsAsync(db,TestContext.Current.CancellationToken));
        user.SetClientIdsJson("invalid"); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await actor.GetAccessibleClientIdsAsync(db,TestContext.Current.CancellationToken));
        db.Users.Remove(user); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await actor.GetAccessibleClientIdsAsync(db,TestContext.Current.CancellationToken));
    }
}
