using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
using SecureClientPortal.Backend.Infrastructure.Modules.Platform;
using System.Text.Json;
namespace SecureClientPortal.Backend.Infrastructure.Modules.UsersRoles.Application;
public sealed class AdminOperationsService(PortalDbContext db, IOptions<StorageOptions> storage, IOptions<AutomationOptions> automation, IConfiguration configuration)
{
    public async Task<AdminOperationsResponse> GetAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var unfinished = db.DocumentSlots.Where(s => s.IsRequired && s.Status != "submitted" && s.Status != "under_review" && s.Status != "accepted" && s.Status != "not_applicable" && db.MonthlyPacks.Any(p => p.Id == s.MonthlyPackId && p.Status != "closed" && p.Status != "complete"));
        var missing = await unfinished.CountAsync(ct);
        var overdue = await unfinished.CountAsync(s => s.DueDateUtc < now, ct);
        var overdueRequests = await db.Requests.CountAsync(x => x.DueDateUtc < now && (x.Status == "open" || x.Status == "waiting_on_client" || x.Status == "waiting_on_accountant" || x.Status == "overdue"), ct);
        var underReview = await db.MonthlyPacks.CountAsync(x => x.Status == "under_review", ct);
        var closed = await db.MonthlyPacks.CountAsync(x => x.Status == "closed" || x.Status == "complete", ct);
        var pending = await db.NotificationEmailDeliveries.CountAsync(x => x.Status == "pending" || x.Status == "sending", ct);
        var failed = await db.NotificationEmailDeliveries.CountAsync(x => x.Status == "failed", ct);
        long? bytes = null;
        try
        {
            if (Directory.Exists(storage.Value.RootPath)) bytes = Directory.EnumerateFiles(storage.Value.RootPath, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }).Sum(path => new FileInfo(path).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        async Task<OperationResult?> Result(string key)
        {
            var setting = await db.SystemSettings.FindAsync([key], ct);
            if (setting is null) return null;
            try { return JsonSerializer.Deserialize<OperationResult>(setting.ValueJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)); } catch (JsonException) { return null; }
        }
        var emailReady = configuration.GetValue<bool>("AccessEmail:Enabled") && string.Equals(configuration["AccessEmail:DeliveryMode"], "smtp", StringComparison.OrdinalIgnoreCase);
        return new(now, "single_firm", missing, overdue, overdueRequests, underReview, closed, pending, failed, emailReady, emailReady && configuration.GetValue<bool>("NotificationEmail:Enabled"), bytes.HasValue, bytes, storage.Value.TotalQuotaBytes, automation.Value.Enabled, await Result("operations.automation"), await Result("operations.backup"), await Result("operations.restore"));
    }
}
