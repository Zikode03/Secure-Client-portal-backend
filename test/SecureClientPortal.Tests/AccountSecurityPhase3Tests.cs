using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Auth;
using SecureClientPortal.Backend.Application.Identity;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Infrastructure.Modules.Auth.Application;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Tests;
public sealed class AccountSecurityPhase3Tests
{
    private const string Password = "A unique portal passphrase 2026!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();
    private static DefaultHttpContext Context() => new() { RequestServices = Services };
    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(20000000000L, "65353130")]
    public void TotpMatchesRfc6238(long seconds, string expected)
    {
        var secret = Totp.Encode(Encoding.ASCII.GetBytes("12345678901234567890"));
        Assert.Equal(expected, Totp.Code(secret, seconds / 30, 8));
    }
    [Fact]
    public void TotpRejectsReplayAndWrongCodes()
    {
        var secret = Totp.NewSecret(); var now = DateTimeOffset.UtcNow; var step = now.ToUnixTimeSeconds() / 30;
        var code = Totp.Code(secret, step);
        Assert.Equal(step, Totp.Verify(secret, code, -1, now));
        Assert.Null(Totp.Verify(secret, code, step, now));
        Assert.Null(Totp.Verify(secret, "abcdef", -1, now));
    }
    [Fact]
    public async Task PasswordScreeningUsesPrefixOnlyAndRejectsCompromisedPasswords()
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Password)));
        var handler = new Handler(request => {
            Assert.Equal("https://api.pwnedpasswords.com/range/" + hash[..5], request.RequestUri!.AbsoluteUri);
            Assert.Equal("true", request.Headers.GetValues("Add-Padding").Single());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hash[5..] + ":12") };
        });
        await Assert.ThrowsAsync<AppValidationException>(() => new PasswordPolicy(new HttpClient(handler)).ValidateAsync(Password, Ct));
    }
    [Theory]
    [InlineData("")]
    [InlineData("short password")]
    public async Task ShortPasswordsAreRejectedWithoutNetwork(string password)
    {
        await Assert.ThrowsAsync<AppValidationException>(() => new PasswordPolicy(new HttpClient(new Handler(_ => throw new Exception("Must not send")))).ValidateAsync(password, Ct));
    }
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.OK, "invalid")]
    public async Task ScreeningFailsClosed(HttpStatusCode status, string body)
    {
        var policy = new PasswordPolicy(new HttpClient(new Handler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) })));
        await Assert.ThrowsAsync<HttpRequestException>(() => policy.ValidateAsync(Password, Ct));
    }
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Accountant)]
    public async Task StaffCannotReceiveSessionUntilMfaAndRecoveryIsSingleUse(UserRole role)
    {
        await using var fixture = new Fixture(role);
        var context = Context();
        var result = await fixture.Service.LoginAsync(new(fixture.User.Email, Password), context, Ct);
        var challenge = JsonSerializer.SerializeToElement(result.Value);
        Assert.True(challenge.GetProperty("mfaRequired").GetBoolean());
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
        Assert.Empty(await fixture.Db.UserSessions.ToListAsync(Ct));
        var key = challenge.GetProperty("setupKey").GetString()!;
        var token = challenge.GetProperty("challengeToken").GetString()!;
        var code = Totp.Code(key, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var verified = await fixture.Service.VerifyMfaAsync(new(token, code), context, Ct);
        Assert.Null(verified.Error);
        Assert.True((await fixture.Db.UserSessions.SingleAsync(Ct)).MfaVerified);
        var recovery = JsonSerializer.SerializeToElement(verified.Value).GetProperty("recoveryCodes")[0].GetString()!;
        var replay = await fixture.Service.VerifyMfaAsync(new(token, code), Context(), Ct);
        Assert.True(replay.Unauthorized);
        var next = JsonSerializer.SerializeToElement((await fixture.Service.LoginAsync(new(fixture.User.Email, Password), Context(), Ct)).Value);
        Assert.Equal(JsonValueKind.Null, next.GetProperty("setupKey").ValueKind);
        var recovered = await fixture.Service.VerifyMfaAsync(new(next.GetProperty("challengeToken").GetString()!, recovery, true), Context(), Ct);
        Assert.True(JsonSerializer.SerializeToElement(recovered.Value).GetProperty("mfaRequired").GetBoolean());
        Assert.NotNull((await fixture.Db.UserSessions.SingleAsync(Ct)).RevokedAtUtc);
        Assert.DoesNotContain(AccessTokenCodec.HashToken(recovery), (await fixture.Db.AccountSecurities.SingleAsync(Ct)).RecoveryHashesJson);
        Assert.True((await fixture.Service.VerifyMfaAsync(new(next.GetProperty("challengeToken").GetString()!, recovery, true), Context(), Ct)).Unauthorized);
    }
    [Fact]
    public async Task LockoutSurvivesNewServiceAndExpires()
    {
        await using var fixture = new Fixture(UserRole.Client);
        for (var i=0; i<5; i++) Assert.True((await fixture.Service.LoginAsync(new(fixture.User.Email,"incorrect"), Context(),Ct)).Unauthorized);
        var result = await fixture.NewService().LoginAsync(new(fixture.User.Email,Password),Context(),Ct);
        Assert.Equal(429,result.StatusCode);
        var state = await fixture.Db.AccountSecurities.SingleAsync(Ct); state.LockedUntilUtc = DateTime.UtcNow.AddSeconds(-1);
        await fixture.Db.SaveChangesAsync(Ct);
        Assert.Null((await fixture.NewService().LoginAsync(new(fixture.User.Email,Password),Context(),Ct)).Error);
    }
    [Fact]
    public async Task ExpiredChallengeAndNonMfaRefreshAreRejected()
    {
        await using var fixture = new Fixture(UserRole.Admin);
        var challenge = JsonSerializer.SerializeToElement((await fixture.Service.LoginAsync(new(fixture.User.Email,Password),Context(),Ct)).Value);
        var state = await fixture.Db.AccountSecurities.SingleAsync(Ct); state.ChallengeExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
        await fixture.Db.SaveChangesAsync(Ct);
        Assert.True((await fixture.Service.VerifyMfaAsync(new(challenge.GetProperty("challengeToken").GetString()!,"123456"),Context(),Ct)).Unauthorized);
        var session = UserSession.Start(Guid.NewGuid(),fixture.User.Id,Guid.NewGuid(),DateTime.UtcNow.AddHours(1),null,null);
        fixture.Db.UserSessions.Add(session);
        var raw = AccessTokenCodec.GenerateToken();
        fixture.Db.UserAccessTokens.Add(UserAccessToken.Create(Guid.NewGuid(),fixture.User.Id,"refresh",AccessTokenCodec.HashToken(raw),DateTime.UtcNow.AddDays(1),session.Id));
        await fixture.Db.SaveChangesAsync(Ct);
        Assert.Equal("MFA_REQUIRED",(await fixture.Service.RefreshAsync(new(raw),Context(),Ct)).ErrorCode);
    }
    [Fact]
    public async Task ResetRequestDoesNotDisableAccountOrLeakExistenceAndLinkExpiresInThirtyMinutes()
    {
        await using var fixture = new Fixture(UserRole.Admin);
        var result = await fixture.Service.ForgotPasswordAsync(new(fixture.User.Email),Context(),Ct);
        var unknown = await fixture.Service.ForgotPasswordAsync(new("unknown@example.test"),Context(),Ct);
        Assert.Equal(JsonSerializer.Serialize(result.Value),JsonSerializer.Serialize(unknown.Value));
        Assert.Equal("active",UserSecurityProfile.GetStatus(fixture.User.SecurityJson));
        Assert.InRange((fixture.Mail.Expires - DateTime.UtcNow).TotalMinutes,29,31);
        Assert.Single(await fixture.Db.UserAccessTokens.ToListAsync(Ct));
        await fixture.Service.ForgotPasswordAsync(new(fixture.User.Email),Context(),Ct);
        Assert.Single(await fixture.Db.UserAccessTokens.ToListAsync(Ct));
    }

    [Fact]
    public async Task EmailPasswordResetPreservesMfaAndCannotReplaySetupToken()
    {
        await using var fixture = new Fixture(UserRole.Admin);
        var challenge = JsonSerializer.SerializeToElement((await fixture.Service.LoginAsync(new(fixture.User.Email,Password),Context(),Ct)).Value);
        var key = challenge.GetProperty("setupKey").GetString()!;
        await fixture.Service.VerifyMfaAsync(new(challenge.GetProperty("challengeToken").GetString()!,Totp.Code(key,DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30)),Context(),Ct);
        var oldSecret = (await fixture.Db.AccountSecurities.SingleAsync(Ct)).MfaSecret;
        await fixture.Service.ForgotPasswordAsync(new(fixture.User.Email),Context(),Ct);
        var raw = fixture.Mail.Url!.Split("token=")[1];
        var context = Context();
        var response = await fixture.Service.CompleteInviteAsync(new(fixture.User.Email,raw,fixture.User.FullName,"An entirely new passphrase 2026!"),context,Ct);
        var body = JsonSerializer.SerializeToElement(response.Value);
        Assert.True(body.GetProperty("mfaRequired").GetBoolean());
        Assert.Equal(JsonValueKind.Null,body.GetProperty("setupKey").ValueKind);
        Assert.Equal(0,context.Response.Headers.SetCookie.Count);
        Assert.Equal(oldSecret,(await fixture.Db.AccountSecurities.SingleAsync(Ct)).MfaSecret);
        Assert.All(await fixture.Db.UserSessions.ToListAsync(Ct),session=>Assert.NotNull(session.RevokedAtUtc));
        Assert.True((await fixture.Service.CompleteInviteAsync(new(fixture.User.Email,raw,fixture.User.FullName,Password),Context(),Ct)).Unauthorized);
    }
    [Theory]
    [InlineData(UserRole.Client)]
    [InlineData(UserRole.Accountant)]
    [InlineData(UserRole.Admin)]
    public async Task PersonalProfilePersistsOnlyOwnDetailsAndPreservesAccess(UserRole role)
    {
        await using var fixture = new Fixture(role);
        fixture.User.SetProfileJson("{\"company\":\"Original firm\",\"managedFlag\":true}");
        var other = User.CreateInvited(Guid.NewGuid(), "Other User", "other@example.test", UserRole.Client, fixture.User.PasswordHash, "[]", null);
        fixture.Db.Users.Add(other);
        await fixture.Db.SaveChangesAsync(Ct);
        var actor = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, fixture.User.Id.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, fixture.User.Role),
            new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        }, "test"));
        var result = await fixture.Service.UpdateProfileAsync(new("  Updated Name  ", "  Finance lead ", "+27 11 123 4567"), actor, Ct);
        Assert.Null(result.Error);
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Users.SingleAsync(x => x.Id == fixture.User.Id, Ct);
        Assert.Equal("Updated Name", saved.FullName);
        Assert.Equal(role.ToStorageValue(), saved.Role);
        Assert.Equal("security@example.test", saved.Email);
        Assert.Equal("[]", saved.ClientIdsJson);
        var profile = JsonSerializer.Deserialize<JsonElement>(saved.ProfileJson!);
        Assert.Equal("Finance lead", profile.GetProperty("title").GetString());
        Assert.True(profile.GetProperty("managedFlag").GetBoolean());
        Assert.Equal("Original firm", profile.GetProperty("company").GetString());
        Assert.Equal("Other User", (await fixture.Db.Users.SingleAsync(x => x.Id == other.Id, Ct)).FullName);
        var me = JsonSerializer.SerializeToElement((await fixture.NewService().MeAsync(actor, Ct)).Value).GetProperty("user");
        Assert.Equal("+27 11 123 4567", me.GetProperty("phone").GetString());
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.Action == "user.profile_updated").ToListAsync(Ct));
        var invalid = await fixture.Service.UpdateProfileAsync(new(" ", "", ""), actor, Ct);
        Assert.Equal("INVALID_PROFILE", invalid.ErrorCode);
        Assert.Equal("Updated Name", saved.FullName);
        var unauthenticated = await fixture.Service.UpdateProfileAsync(new("Changed", "", ""), new System.Security.Claims.ClaimsPrincipal(), Ct);
        Assert.True(unauthenticated.Unauthorized);
    }

    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(reply(request)); }
    private sealed class Mail : IAccessEmailSender
    {
        public DateTime Expires;
        public string? Url;
        public Task<AccessEmailDispatchResult> SendInviteAsync(string email,string name,string url,DateTime expires,CancellationToken ct) => Task.FromResult(new AccessEmailDispatchResult("smtp"));
        public Task<AccessEmailDispatchResult> SendPasswordResetAsync(string email,string name,string url,DateTime expires,CancellationToken ct) { Expires=expires; Url=url; return Task.FromResult(new AccessEmailDispatchResult("smtp")); }
    }
    private sealed class Links : IAccessLinkBuilder
    {
        public string BuildPasswordResetUrl(string email,string token) => "https://portal.example.test/reset?token="+token;
        public string BuildSetupUrl(string email,string token) => "https://portal.example.test/setup?token="+token;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public PortalDbContext Db { get; } = new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public User User { get; }
        public Mail Mail { get; } = new();
        private readonly EphemeralDataProtectionProvider protection = new();
        public AuthService Service => NewService();
        public Fixture(UserRole role)
        {
            User = User.CreateInvited(Guid.NewGuid(),"Security Test","security@example.test",role,PasswordHasher.Hash(Password),"[]",null);
            User.CompleteSetup(User.FullName,User.PasswordHash);
            Db.Users.Add(User); Db.RoleDefinitions.Add(RoleDefinition.Create(User.Role,User.Role,User.Role,"[]",true)); Db.SaveChanges();
        }
        public AuthService NewService() => new(Db,Options.Create(new JwtOptions { SigningKey=new string('k',48),Issuer="test",Audience="test" }),Mail,new Links(),
            new PasswordPolicy(new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent(new string('A',35)+":0")}))),protection);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
