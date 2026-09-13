using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Data;
using System.Data;
namespace SecureClientPortal.Backend.Auth;

// SQL Server application locks serialize guesses for an account across API instances.
public sealed class AccountAttemptLock(PortalDbContext db, string key) : IAsyncDisposable
{
    public static async Task<AccountAttemptLock> AcquireAsync(PortalDbContext db, string email, CancellationToken ct)
    {
        var gate = new AccountAttemptLock(db, "auth:" + AccessTokenCodec.HashToken(email.Trim().ToLowerInvariant()));
        if (!db.Database.IsSqlServer()) return gate; // In-memory unit tests only.
        await db.Database.OpenConnectionAsync(ct);
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource=@key, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=5000; SELECT @r;";
        var p = command.CreateParameter(); p.ParameterName = "@key"; p.Value = gate.Key; command.Parameters.Add(p);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) { await db.Database.CloseConnectionAsync(); throw new InvalidOperationException("Account authentication is busy."); }
        return gate;
    }
    private string Key => key;
    public async ValueTask DisposeAsync()
    {
        if (!db.Database.IsSqlServer()) return;
        try {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "EXEC sp_releaseapplock @Resource=@key, @LockOwner='Session';";
            var p = command.CreateParameter(); p.ParameterName = "@key"; p.Value = key; command.Parameters.Add(p);
            await command.ExecuteNonQueryAsync();
        } finally { await db.Database.CloseConnectionAsync(); }
    }
}
