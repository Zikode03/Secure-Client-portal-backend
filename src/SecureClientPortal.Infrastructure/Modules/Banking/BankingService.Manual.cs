using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Modules.Banking;
using SecureClientPortal.Backend.Domain.Modules.Banking;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Banking;

public sealed partial class BankingService
{
    public async Task<BankingOperationResult<BankingOverviewDto>> AddManualAccountAsync(
        ManualBankAccountRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var resolved = await ResolveClientIdAsync(request.ClientId, user, ct);
        if (resolved.forbidden) return BankingOperationResult<BankingOverviewDto>.Denied();
        if (resolved.clientId is not Guid clientId)
            return BankingOperationResult<BankingOverviewDto>.Fail("Select a business first.");
        if (string.IsNullOrWhiteSpace(request.BankName) || request.BankName.Length > 120 ||
            string.IsNullOrWhiteSpace(request.AccountName) || request.AccountName.Length > 200 ||
            request.RegistrationId == Guid.Empty || request.LastFourDigits is null || request.LastFourDigits.Length != 4 || !request.LastFourDigits.All(char.IsAsciiDigit))
            return BankingOperationResult<BankingOverviewDto>.Fail("Enter a bank, account label and exactly four account digits.");

        await using var transaction = await BeginManualTransactionAsync(ct);
        var key = $"manual:{clientId:N}:{request.RegistrationId:N}";
        var connection = await bankingDb.BankConnections.FirstOrDefaultAsync(
            x => x.ClientId == clientId && x.Provider == "manual_csv" && x.ExternalConnectionId == key, ct);
        BankAccount account;
        if (connection is null)
        {
            connection = BankConnection.Create(clientId, "manual_csv", key, UtcNow);
            connection.Disconnect(UtcNow);
            bankingDb.BankConnections.Add(connection);
            account = BankAccount.Create(connection.Id, clientId, key,
                request.BankName, request.AccountName, "manual", "****" + request.LastFourDigits,
                "ZAR", null, null, UtcNow);
            bankingDb.BankAccounts.Add(account);
            await bankingDb.SaveChangesAsync(ct);
        }
        else
        {
            account = await bankingDb.BankAccounts.SingleAsync(x => x.BankConnectionId == connection.Id, ct);
            if (account.BankName != request.BankName.Trim() || account.AccountName != request.AccountName.Trim() ||
                account.AccountNumberMasked != "****" + request.LastFourDigits)
                return BankingOperationResult<BankingOverviewDto>.Fail("This registration identifier was already used for different account details. Refresh before registering another account.");
        }
        await ReconcileOpenPackBankSlotsAsync(clientId, false, ct);
        await portalDb.WriteAuditLogAsync(user, "banking.manual_account_saved", "bank_account", account.Id,
            clientId, System.Text.Json.JsonSerializer.Serialize(new { request.BankName, request.AccountName, request.LastFourDigits, request.RegistrationId }), ct);
        var overview = await BuildOverviewAsync(clientId, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return BankingOperationResult<BankingOverviewDto>.Ok(overview);
    }

    public async Task<BankingOperationResult<ManualTransactionImportResult>> ImportManualTransactionsAsync(
        ManualTransactionImportRequest request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var account = await bankingDb.BankAccounts.FirstOrDefaultAsync(x => x.Id == request.AccountId, ct);
        if (account is null) return BankingOperationResult<ManualTransactionImportResult>.Fail("Account not found.");
        if (!await CanAccessClientAsync(account.ClientId, user, ct)) return BankingOperationResult<ManualTransactionImportResult>.Denied();
        if (account.AccountType != "manual") return BankingOperationResult<ManualTransactionImportResult>.Fail("Choose a manually registered account.");
        if (request.Rows is null || request.Rows.Count is < 1 or > 5000 ||
            string.IsNullOrWhiteSpace(request.SourceName) || request.SourceName.Length > 200)
            return BankingOperationResult<ManualTransactionImportResult>.Fail("Choose a CSV with 1–5,000 rows and a source filename up to 200 characters.");

        var prepared = new List<BankTransaction>();
        var occurrences = new Dictionary<string, int>();
        foreach (var row in request.Rows)
        {
            if (row is null || !DateTime.TryParseExact(row.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ||
                date.Year < 2000 || date.Year > 2200 || string.IsNullOrWhiteSpace(row.Description) || row.Description.Length > 1000 ||
                row.Reference is null || row.Reference.Length > 500 || row.Amount == 0 ||
                row.Amount <= -1_000_000_000_000_000m || row.Amount >= 1_000_000_000_000_000m ||
                row.Balance is decimal balance && (balance <= -1_000_000_000_000_000m || balance >= 1_000_000_000_000_000m) ||
                decimal.Round(row.Amount, 4) != row.Amount || row.Balance is decimal b && decimal.Round(b, 4) != b)
                return BankingOperationResult<ManualTransactionImportResult>.Fail($"Invalid transaction at row {prepared.Count + 1}. Check date, description and amounts (maximum four decimals).");
            var canonical = System.Text.Json.JsonSerializer.Serialize(new {
                row.Date, Description = row.Description.Trim(), Reference = row.Reference.Trim(),
                Amount = row.Amount.ToString("0.####", CultureInfo.InvariantCulture),
                Balance = row.Balance?.ToString("0.####", CultureInfo.InvariantCulture) });
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            var occurrence = occurrences.GetValueOrDefault(hash) + 1;
            occurrences[hash] = occurrence;
            // Occurrences preserve legitimate identical rows within a statement.
            var externalId = $"manual:{hash}:{occurrence}";
            prepared.Add(BankTransaction.Create(account.Id, account.ClientId, externalId, date, null,
                row.Description, row.Reference, Math.Abs(row.Amount), row.Amount < 0 ? "debit" : "credit",
                row.Balance, "Manual CSV — unreviewed", UtcNow));
        }

        await using var transaction = await BeginManualTransactionAsync(ct);
        var existing = (await bankingDb.BankTransactions.Where(x => x.BankAccountId == account.Id)
            .Select(x => x.ExternalTransactionId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var added = prepared.Where(x => !existing.Contains(x.ExternalTransactionId)).ToList();
        var result = new ManualTransactionImportResult(added.Count, prepared.Count - added.Count, prepared.Count,
            added.Where(x => x.Direction == "credit").Sum(x => x.Amount),
            added.Where(x => x.Direction == "debit").Sum(x => x.Amount), request.Preview);
        if (!request.Preview)
        {
            bankingDb.BankTransactions.AddRange(added);
            var run = BankSyncRun.Start(account.BankConnectionId, account.ClientId, "manual_csv", UtcNow);
            // No provider coverage is asserted by a user-supplied statement.
            run.Complete(added.Count, null, null, UtcNow);
            bankingDb.BankSyncRuns.Add(run);
            try
            {
                await bankingDb.SaveChangesAsync(ct);
                await portalDb.WriteAuditLogAsync(user, "banking.manual_transactions_imported", "bank_account", account.Id,
                    account.ClientId, System.Text.Json.JsonSerializer.Serialize(new { request.SourceName, result.Imported, result.Duplicates, result.TotalRows }), ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                return BankingOperationResult<ManualTransactionImportResult>.Fail("Import could not be saved. Refresh and preview again before retrying.");
            }
        }
        return BankingOperationResult<ManualTransactionImportResult>.Ok(result);
    }

    private async Task<IDbContextTransaction?> BeginManualTransactionAsync(CancellationToken ct)
    {
        if (!bankingDb.Database.IsRelational()) return null;
        // Both contexts share one connection/transaction: transaction rows, statement
        // slots and audit entries either commit together or all roll back.
        portalDb.Database.SetDbConnection(bankingDb.Database.GetDbConnection(), contextOwnsConnection: false);
        var transaction = await bankingDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await portalDb.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        return transaction;
    }

    private async Task EnsureManualAccountSlotsAsync(MonthlyPack pack, CancellationToken ct)
    {
        if (config.IntegrationEnabled) return;
        var accounts = await bankingDb.BankAccounts.Where(x => x.ClientId == pack.ClientId && x.AccountType == "manual").ToListAsync(ct);
        var slots = await portalDb.DocumentSlots.Where(x => x.MonthlyPackId == pack.Id).ToListAsync(ct);
        foreach (var account in accounts)
        {
            var category = $"bank_statement_account_{account.Id:N}";
            var slot = slots.FirstOrDefault(x => x.Category == category);
            if (slot is null)
            {
                slot = DocumentSlot.Create(Guid.NewGuid(), pack.Id, pack.ClientId, category,
                    $"Bank Statement — {ShortLabel(account.BankName, 60)}: {ShortLabel(account.AccountName, 90)} ({account.AccountNumberMasked})", true, null, UtcNow);
                portalDb.DocumentSlots.Add(slot);
            }
            BankStatementSlotPolicy.Apply(slot, false, slot.CurrentDocumentId.HasValue || slot.Status is not ("not_started" or "not_applicable"));
        }
    }

    private static string ShortLabel(string value, int limit) => value.Length <= limit ? value : value[..limit];
}
