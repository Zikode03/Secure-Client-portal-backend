namespace SecureClientPortal.Backend.Domain.Modules.Banking;

public sealed class BankConnection
{
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ExternalConnectionId { get; private set; } = string.Empty;
    public string Status { get; private set; } = "connecting";
    public DateTime ConnectedAtUtc { get; private set; }
    public DateTime? LastSyncedAtUtc { get; private set; }
    public DateTime? ConsentExpiresAtUtc { get; private set; }
    public DateTime? DisconnectedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    private BankConnection() { }

    public static BankConnection Create(Guid clientId, string provider, string externalConnectionId, DateTime nowUtc, DateTime? consentExpiresAtUtc = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Provider = provider.Trim(),
            ExternalConnectionId = externalConnectionId.Trim(),
            Status = "connected",
            ConnectedAtUtc = nowUtc,
            ConsentExpiresAtUtc = consentExpiresAtUtc
        };

    public void MarkSynced(DateTime atUtc)
    {
        LastSyncedAtUtc = atUtc;
        Status = "connected";
        FailureReason = null;
    }

    public void MarkNeedsAttention(string reason)
    {
        Status = "needs_attention";
        FailureReason = reason;
    }

    public void Disconnect(DateTime atUtc)
    {
        Status = "disconnected";
        DisconnectedAtUtc = atUtc;
    }
}

public sealed class BankAccount
{
    public Guid Id { get; private set; }
    public Guid BankConnectionId { get; private set; }
    public Guid ClientId { get; private set; }
    public string ExternalAccountId { get; private set; } = string.Empty;
    public string BankName { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public string AccountType { get; private set; } = string.Empty;
    public string AccountNumberMasked { get; private set; } = string.Empty;
    public string Currency { get; private set; } = "ZAR";
    public decimal? CurrentBalance { get; private set; }
    public decimal? AvailableBalance { get; private set; }
    public DateTime LastUpdatedAtUtc { get; private set; }

    private BankAccount() { }

    public static BankAccount Create(Guid connectionId, Guid clientId, string externalAccountId, string bankName, string accountName, string accountType, string maskedNumber, string currency, decimal? currentBalance, decimal? availableBalance, DateTime nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            BankConnectionId = connectionId,
            ClientId = clientId,
            ExternalAccountId = externalAccountId.Trim(),
            BankName = bankName.Trim(),
            AccountName = accountName.Trim(),
            AccountType = accountType.Trim(),
            AccountNumberMasked = maskedNumber.Trim(),
            Currency = string.IsNullOrWhiteSpace(currency) ? "ZAR" : currency.Trim().ToUpperInvariant(),
            CurrentBalance = currentBalance,
            AvailableBalance = availableBalance,
            LastUpdatedAtUtc = nowUtc
        };

    public void RefreshBalances(decimal? currentBalance, decimal? availableBalance, DateTime atUtc)
    {
        CurrentBalance = currentBalance;
        AvailableBalance = availableBalance;
        LastUpdatedAtUtc = atUtc;
    }
}

public sealed class BankTransaction
{
    public Guid Id { get; private set; }
    public Guid BankAccountId { get; private set; }
    public Guid ClientId { get; private set; }
    public string ExternalTransactionId { get; private set; } = string.Empty;
    public DateTime TransactionDateUtc { get; private set; }
    public DateTime? PostedDateUtc { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Direction { get; private set; } = string.Empty;
    public decimal? Balance { get; private set; }
    public string ProviderCategory { get; private set; } = string.Empty;
    public DateTime ImportedAtUtc { get; private set; }

    private BankTransaction() { }

    public static BankTransaction Create(Guid accountId, Guid clientId, string externalTransactionId, DateTime transactionDateUtc, DateTime? postedDateUtc, string description, string reference, decimal amount, string direction, decimal? balance, string providerCategory, DateTime importedAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            BankAccountId = accountId,
            ClientId = clientId,
            ExternalTransactionId = externalTransactionId.Trim(),
            TransactionDateUtc = transactionDateUtc,
            PostedDateUtc = postedDateUtc,
            Description = description.Trim(),
            Reference = reference.Trim(),
            Amount = amount,
            Direction = direction.Trim().ToLowerInvariant(),
            Balance = balance,
            ProviderCategory = providerCategory.Trim(),
            ImportedAtUtc = importedAtUtc
        };
}

public sealed class BankSyncRun
{
    public Guid Id { get; private set; }
    public Guid BankConnectionId { get; private set; }
    public Guid ClientId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public string Status { get; private set; } = "running";
    public int TransactionsReceived { get; private set; }
    public DateTime? FromDateUtc { get; private set; }
    public DateTime? ToDateUtc { get; private set; }
    public string? ErrorMessage { get; private set; }

    private BankSyncRun() { }

    public static BankSyncRun Start(Guid connectionId, Guid clientId, string provider, DateTime startedAtUtc) =>
        new() { Id = Guid.NewGuid(), BankConnectionId = connectionId, ClientId = clientId, Provider = provider, StartedAtUtc = startedAtUtc };

    public void Complete(int count, DateTime? fromDateUtc, DateTime? toDateUtc, DateTime finishedAtUtc)
    {
        Status = "completed";
        TransactionsReceived = count;
        FromDateUtc = fromDateUtc;
        ToDateUtc = toDateUtc;
        FinishedAtUtc = finishedAtUtc;
    }

    public void Fail(string error, DateTime finishedAtUtc)
    {
        Status = "failed";
        ErrorMessage = error;
        FinishedAtUtc = finishedAtUtc;
    }
}

public sealed class BankConsentRecord
{
    public Guid Id { get; private set; }
    public Guid BankConnectionId { get; private set; }
    public Guid ClientId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Scope { get; private set; } = string.Empty;
    public DateTime GrantedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    private BankConsentRecord() { }

    public static BankConsentRecord Create(Guid connectionId, Guid clientId, string provider, string scope, DateTime grantedAtUtc, DateTime? expiresAtUtc) =>
        new() { Id = Guid.NewGuid(), BankConnectionId = connectionId, ClientId = clientId, Provider = provider, Scope = scope, GrantedAtUtc = grantedAtUtc, ExpiresAtUtc = expiresAtUtc };

    public void Revoke(DateTime atUtc) => RevokedAtUtc = atUtc;
}
