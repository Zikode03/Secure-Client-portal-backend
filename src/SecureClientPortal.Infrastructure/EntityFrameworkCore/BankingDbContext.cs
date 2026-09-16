using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Domain.Modules.Banking;

namespace SecureClientPortal.Backend.Data;

public sealed class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    public DbSet<BankConnection> BankConnections => Set<BankConnection>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<BankSyncRun> BankSyncRuns => Set<BankSyncRun>();
    public DbSet<BankConsentRecord> BankConsentRecords => Set<BankConsentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BankConnection>(entity =>
        {
            entity.ToTable("AppBankConnections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ExternalConnectionId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.Property(x => x.FailureReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.ClientId, x.Status });
            entity.HasIndex(x => new { x.Provider, x.ExternalConnectionId }).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AppBankConnections_Status",
                "Status IN ('connecting','connected','needs_attention','consent_expiring','sync_failed','disconnected')"));
        });

        modelBuilder.Entity<BankAccount>(entity =>
        {
            entity.ToTable("AppBankAccounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalAccountId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.BankName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.AccountName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AccountType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.AccountNumberMasked).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            entity.Property(x => x.CurrentBalance).HasPrecision(19, 4);
            entity.Property(x => x.AvailableBalance).HasPrecision(19, 4);
            entity.HasIndex(x => new { x.BankConnectionId, x.ExternalAccountId }).IsUnique();
            entity.HasIndex(x => x.ClientId);
        });

        modelBuilder.Entity<BankTransaction>(entity =>
        {
            entity.ToTable("AppBankTransactions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalTransactionId).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(19, 4);
            entity.Property(x => x.Direction).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Balance).HasPrecision(19, 4);
            entity.Property(x => x.ProviderCategory).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => new { x.BankAccountId, x.ExternalTransactionId }).IsUnique();
            entity.HasIndex(x => new { x.ClientId, x.TransactionDateUtc });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AppBankTransactions_Direction",
                "Direction IN ('debit','credit')"));
        });

        modelBuilder.Entity<BankSyncRun>(entity =>
        {
            entity.ToTable("AppBankSyncRuns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(1500);
            entity.HasIndex(x => new { x.BankConnectionId, x.StartedAtUtc });
            entity.HasIndex(x => new { x.ClientId, x.StartedAtUtc });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AppBankSyncRuns_Status",
                "Status IN ('running','completed','failed')"));
        });

        modelBuilder.Entity<BankConsentRecord>(entity =>
        {
            entity.ToTable("AppBankConsentRecords");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.ClientId, x.GrantedAtUtc });
            entity.HasIndex(x => x.BankConnectionId);
        });
    }
}
