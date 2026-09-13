using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
namespace SecureClientPortal.Backend.Api.Modules.Auth;

[ApiController, Route("api/admin/smtp-verification"), Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("auth-account")]
public sealed class SmtpVerificationController(PortalDbContext db, AccessEmailSender sender, Microsoft.Extensions.Options.IOptions<AccessEmailOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.GetUserId()!.Value], ct);
        var state = await db.AccountSecurities.FindAsync([user!.Id], ct);
        return Ok(new { verifiedAtUtc = state?.SmtpProbeHash == "verified:" + Fingerprint(user.Email) ? state.SmtpVerifiedUtc : null });
    }
    private string Fingerprint(string email) => AccessTokenCodec.HashToken(
        System.Text.Json.JsonSerializer.Serialize(new { recipient = email, configuration = options.Value }));

    [HttpPost]
    public async Task<IActionResult> Send(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.GetUserId()!.Value], ct);
        var state = await db.AccountSecurities.FindAsync([user!.Id], ct);
        if (state is null) { state = new AccountSecurity { Id = user.Id }; db.AccountSecurities.Add(state); }
        var code = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        state.SmtpProbeHash = AccessTokenCodec.HashToken(code) + ":" + Fingerprint(user.Email); state.SmtpProbeExpiresUtc = DateTime.UtcNow.AddMinutes(10);
        state.SmtpVerifiedUtc = null; state.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        var result = await sender.SendVerificationAsync(user.Email, user.FullName, code, ct);
        if (result.DeliveryMode != "smtp") return StatusCode(503, new { message = "SMTP delivery is not enabled." });
        return Ok(new { message = "Verification email sent to your account email. Enter its code to confirm receipt." });
    }
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ConfirmSmtpRequest request, CancellationToken ct)
    {
        var state = await db.AccountSecurities.FindAsync([User.GetUserId()!.Value], ct);
        var user = await db.Users.FindAsync([User.GetUserId()!.Value], ct);
        if (state?.SmtpProbeHash is null || state.SmtpProbeExpiresUtc <= DateTime.UtcNow ||
            request.Code?.Length != 32 || state.SmtpProbeHash != AccessTokenCodec.HashToken(request.Code.ToUpperInvariant()) + ":" + Fingerprint(user!.Email))
            return BadRequest(new { message = "Invalid or expired verification code." });
        state.SmtpProbeHash = "verified:" + Fingerprint(user!.Email); state.SmtpProbeExpiresUtc = null; state.SmtpVerifiedUtc = DateTime.UtcNow;
        state.Version = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await db.WriteAuditLogAsync(User, "security.smtp_verified", "user", state.Id, null, null, ct);
        return Ok(new { verifiedAtUtc = state.SmtpVerifiedUtc });
    }
}
public record ConfirmSmtpRequest(string Code);
