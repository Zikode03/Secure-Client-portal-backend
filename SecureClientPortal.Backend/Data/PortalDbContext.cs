using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Data;

public class PortalDbContext : DbContext
{
    public PortalDbContext(DbContextOptions<PortalDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentComment> DocumentComments => Set<DocumentComment>();
    public DbSet<FilingRule> FilingRules => Set<FilingRule>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<RequestItem> Requests => Set<RequestItem>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<ClientAssignment> ClientAssignments => Set<ClientAssignment>();
    public DbSet<MonthlyPack> MonthlyPacks => Set<MonthlyPack>();
    public DbSet<DocumentSlot> DocumentSlots => Set<DocumentSlot>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<ReviewDecision> ReviewDecisions => Set<ReviewDecision>();
    public DbSet<RequestComment> RequestComments => Set<RequestComment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ComplianceCategory> ComplianceCategories => Set<ComplianceCategory>();
    public DbSet<ComplianceItem> ComplianceItems => Set<ComplianceItem>();
    public DbSet<ComplianceReminder> ComplianceReminders => Set<ComplianceReminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("AppUsers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ClientIdsJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.ProfileJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.SecurityJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppUsers_Role", "Role IN ('admin','accountant','client')");
            });
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.ToTable("AppClients");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.AssignedAccountantId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PrimaryContact).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.AssignedAccountantId);
            entity.HasIndex(x => x.Status);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppClients_Status", "Status IN ('pending','active','at_risk','archived')");
                table.HasCheckConstraint("CK_AppClients_ComplianceHealth", "ComplianceHealth >= 0 AND ComplianceHealth <= 100");
            });
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("AppDocuments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(260).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.DocumentSlotId).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.StorageKey).HasMaxLength(500);
            entity.Property(x => x.UploadedByUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.FiledByUserId).HasMaxLength(100);
            entity.Property(x => x.CurrentVersionNumber).HasDefaultValue(1);
            entity.Property(x => x.UploadedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.ClientId, x.IsFiled });
            entity.HasIndex(x => new { x.ClientId, x.Status });
            entity.HasIndex(x => x.UploadedAtUtc);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppDocuments_Status", "Status IN ('draft','pending','under_review','accepted','rejected','filed')");
            });
        });

        modelBuilder.Entity<DocumentComment>(entity =>
        {
            entity.ToTable("AppDocumentComments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.DocumentId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AuthorUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AuthorRole).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.DocumentId, x.CreatedAtUtc });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppDocumentComments_AuthorRole", "AuthorRole IN ('admin','accountant','client')");
            });
        });

        modelBuilder.Entity<FilingRule>(entity =>
        {
            entity.ToTable("AppFilingRules");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(280).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.Category).IsUnique();
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("AppTasks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedByUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.ClientId, x.Status });
            entity.HasIndex(x => x.DueDateUtc);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppTasks_Status", "Status IN ('todo','in_progress','blocked','done')");
                table.HasCheckConstraint("CK_AppTasks_Priority", "Priority IN ('low','medium','high','urgent')");
            });
        });

        modelBuilder.Entity<RequestItem>(entity =>
        {
            entity.ToTable("AppRequests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.RequestType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.RelatedDocumentId).HasMaxLength(100);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.RequestedByUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ResolvedByUserId).HasMaxLength(100);
            entity.Property(x => x.RequestedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.ClientId, x.Status });
            entity.HasIndex(x => x.DueDateUtc);
            entity.HasIndex(x => x.RelatedDocumentId);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppRequests_Status", "Status IN ('open','awaiting_client','awaiting_accountant','resolved')");
                table.HasCheckConstraint("CK_AppRequests_Priority", "Priority IN ('low','medium','high','urgent')");
                table.HasCheckConstraint("CK_AppRequests_RequestType", "RequestType IN ('missing_document','reupload','clarification','renewal','signature')");
            });
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("AppSystemSettings");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(120);
            entity.Property(x => x.ValueJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<ClientAssignment>(entity =>
        {
            entity.ToTable("AppClientAssignments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.AccountantUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.AccountantUserId);
            entity.HasIndex(x => x.ClientId);
            entity.HasIndex(x => new { x.AccountantUserId, x.ClientId }).IsUnique();
        });

        modelBuilder.Entity<MonthlyPack>(entity =>
        {
            entity.ToTable("AppMonthlyPacks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.ClientId, x.Year, x.Month }).IsUnique();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppMonthlyPacks_Status", "Status IN ('draft','in_progress','submitted','under_review','completed')");
                table.HasCheckConstraint("CK_AppMonthlyPacks_Month", "Month >= 1 AND Month <= 12");
            });
        });

        modelBuilder.Entity<DocumentSlot>(entity =>
        {
            entity.ToTable("AppDocumentSlots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.MonthlyPackId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IsRequired).HasDefaultValue(true);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CurrentDocumentId).HasMaxLength(100);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.MonthlyPackId);
            entity.HasIndex(x => new { x.MonthlyPackId, x.Category }).IsUnique();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppDocumentSlots_Status", "Status IN ('missing','uploaded','under_review','accepted','rejected','filed')");
            });
        });

        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.ToTable("AppDocumentVersions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.DocumentId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(260).IsRequired();
            entity.Property(x => x.StorageKey).HasMaxLength(500);
            entity.Property(x => x.UploadedByUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.DocumentId);
            entity.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
        });

        modelBuilder.Entity<ReviewDecision>(entity =>
        {
            entity.ToTable("AppReviewDecisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.DocumentId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Decision).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ReviewerUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ReviewerRole).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.DecidedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.DocumentId);
            entity.HasIndex(x => x.DecidedAtUtc);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppReviewDecisions_Decision", "Decision IN ('under_review','accepted','rejected','request_reupload')");
                table.HasCheckConstraint("CK_AppReviewDecisions_ReviewerRole", "ReviewerRole IN ('admin','accountant')");
            });
        });

        modelBuilder.Entity<RequestComment>(entity =>
        {
            entity.ToTable("AppRequestComments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.RequestId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AuthorUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AuthorRole).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.RequestId, x.CreatedAtUtc });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppRequestComments_AuthorRole", "AuthorRole IN ('admin','accountant','client')");
            });
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("AppNotifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.UserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(100);
            entity.Property(x => x.Type).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.LinkUrl).HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAtUtc });
            entity.HasIndex(x => x.ClientId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AppAuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.ActorUserId).HasMaxLength(100);
            entity.Property(x => x.ActorRole).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(100);
            entity.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => new { x.EntityType, x.EntityId });
            entity.HasIndex(x => x.ClientId);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppAuditLogs_ActorRole", "ActorRole IN ('admin','accountant','client','unknown')");
            });
        });

        modelBuilder.Entity<ComplianceCategory>(entity =>
        {
            entity.ToTable("AppComplianceCategories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(60);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<ComplianceItem>(entity =>
        {
            entity.ToTable("AppComplianceItems");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CategoryId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.OwnerUserId).HasMaxLength(100);
            entity.Property(x => x.RiskLevel).HasMaxLength(20).IsRequired();
            entity.Property(x => x.RequiredDocumentCategory).HasMaxLength(80);
            entity.Property(x => x.LinkedDocumentId).HasMaxLength(100);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.ClientId, x.CategoryId });
            entity.HasIndex(x => new { x.ClientId, x.Status });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_AppComplianceItems_Status",
                    "Status IN ('missing','pending','valid','expiring_soon','expired','rejected')");
                table.HasCheckConstraint(
                    "CK_AppComplianceItems_RiskLevel",
                    "RiskLevel IN ('low','medium','high','critical')");
            });
        });

        modelBuilder.Entity<ComplianceReminder>(entity =>
        {
            entity.ToTable("AppComplianceReminders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.ComplianceItemId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.RecipientUserId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.ClientId, x.ScheduledForUtc });
            entity.HasIndex(x => new { x.RecipientUserId, x.Status });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_AppComplianceReminders_Status",
                    "Status IN ('pending','sent','dismissed')");
            });
        });
    }
}



