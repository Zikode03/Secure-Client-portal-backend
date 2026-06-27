using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Application.Documents;
using SecureClientPortal.Backend.Application.Requests;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Data;

public class PortalDbContext : DbContext, IDocumentModuleDbContext, IRequestModuleDbContext
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
    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserAccessToken> UserAccessTokens => Set<UserAccessToken>();
    public DbSet<ComplianceCategory> ComplianceCategories => Set<ComplianceCategory>();
    public DbSet<ComplianceItem> ComplianceItems => Set<ComplianceItem>();
    public DbSet<ComplianceReminder> ComplianceReminders => Set<ComplianceReminder>();
    public DbSet<RequiredDocumentTemplate> RequiredDocumentTemplates => Set<RequiredDocumentTemplate>();
    public DbSet<MonthlyPackTemplate> MonthlyPackTemplates => Set<MonthlyPackTemplate>();
    public DbSet<MonthlyPackTemplateItem> MonthlyPackTemplateItems => Set<MonthlyPackTemplateItem>();
    public DbSet<RequestTemplate> RequestTemplates => Set<RequestTemplate>();
    public DbSet<ReminderRule> ReminderRules => Set<ReminderRule>();
    public DbSet<DeadlineRule> DeadlineRules => Set<DeadlineRule>();
    public DbSet<DocumentAccessLog> DocumentAccessLogs => Set<DocumentAccessLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("AppUsers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
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
            entity.Property(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.AssignedAccountantId).IsRequired();
            entity.Property(x => x.PrimaryContact).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.AssignedAccountantId);
            entity.HasIndex(x => x.Status);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppClients_Status", "Status IN ('active','inactive')");
                table.HasCheckConstraint("CK_AppClients_ComplianceHealth", "ComplianceHealth >= 0 AND ComplianceHealth <= 100");
            });
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("AppDocuments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.MonthlyPackId).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(260).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.DocumentSlotId);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.FileType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StorageKey).HasMaxLength(500);
            entity.Property(x => x.UploadedByUserId).IsRequired();
            entity.Property(x => x.FiledByUserId);
            entity.Property(x => x.CurrentVersionNumber).HasDefaultValue(1);
            entity.Property(x => x.UploadedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.ClientId, x.IsFiled });
            entity.HasIndex(x => new { x.ClientId, x.Status });
            entity.HasIndex(x => x.MonthlyPackId);
            entity.HasIndex(x => x.UploadedAtUtc);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppDocuments_Status", "Status IN ('draft','uploaded','under_review','accepted','rejected','filed')");
            });
        });

        modelBuilder.Entity<DocumentComment>(entity =>
        {
            entity.ToTable("AppDocumentComments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.DocumentId).IsRequired();
            entity.Property(x => x.AuthorUserId).IsRequired();
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
            entity.Property(x => x.Id);
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
            entity.Property(x => x.Id);
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedByUserId).IsRequired();
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
            entity.Property(x => x.Id);
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.RequestType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.RelatedDocumentId);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.RequestedByUserId).IsRequired();
            entity.Property(x => x.ResolvedByUserId);
            entity.Property(x => x.RequestedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.ClientId, x.Status });
            entity.HasIndex(x => x.DueDateUtc);
            entity.HasIndex(x => x.RelatedDocumentId);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppRequests_Status", "Status IN ('open','waiting_on_client','waiting_on_accountant','resolved','overdue')");
                table.HasCheckConstraint("CK_AppRequests_Priority", "Priority IN ('low','medium','high','urgent')");
                table.HasCheckConstraint("CK_AppRequests_RequestType", "RequestType IN ('missing_document','reupload_required','clarification_needed','signature_required','compliance_renewal')");
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
            entity.Property(x => x.Id);
            entity.Property(x => x.AccountantUserId).IsRequired();
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.AccountantUserId);
            entity.HasIndex(x => x.ClientId);
            entity.HasIndex(x => new { x.AccountantUserId, x.ClientId }).IsUnique();
        });

        modelBuilder.Entity<MonthlyPack>(entity =>
        {
            entity.ToTable("AppMonthlyPacks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.SubmittedAtUtc);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.ClientId, x.Year, x.Month }).IsUnique();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppMonthlyPacks_Status", "Status IN ('draft','in_progress','submitted','under_review','completed','reopened')");
                table.HasCheckConstraint("CK_AppMonthlyPacks_Month", "Month >= 1 AND Month <= 12");
            });
        });

        modelBuilder.Entity<DocumentSlot>(entity =>
        {
            entity.ToTable("AppDocumentSlots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.MonthlyPackId).IsRequired();
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IsRequired).HasDefaultValue(true);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CurrentDocumentId);
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
            entity.Property(x => x.Id);
            entity.Property(x => x.DocumentId).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(260).IsRequired();
            entity.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.StoredFileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.FileType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StorageKey).HasMaxLength(500);
            entity.Property(x => x.UploadedByUserId).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.DocumentId);
            entity.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => new { x.DocumentId, x.IsCurrentVersion });
        });

        modelBuilder.Entity<ReviewDecision>(entity =>
        {
            entity.ToTable("AppReviewDecisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.DocumentId).IsRequired();
            entity.Property(x => x.Decision).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ReviewerUserId).IsRequired();
            entity.Property(x => x.ReviewerRole).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.InternalNote).HasMaxLength(2000);
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
            entity.Property(x => x.Id);
            entity.Property(x => x.RequestId).IsRequired();
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.AuthorUserId).IsRequired();
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
            entity.Property(x => x.Id);
            entity.Property(x => x.UserId).IsRequired();
            entity.Property(x => x.ClientId);
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
            entity.Property(x => x.Id);
            entity.Property(x => x.ActorUserId);
            entity.Property(x => x.ActorRole).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.EntityId).IsRequired();
            entity.Property(x => x.ClientId);
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

        modelBuilder.Entity<RoleDefinition>(entity =>
        {
            entity.ToTable("AppRoles");
            entity.HasKey(x => x.Name);
            entity.Property(x => x.Name).HasMaxLength(80);
            entity.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(30).IsRequired();
            entity.Property(x => x.PermissionsJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.DisplayName).IsUnique();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppRoles_Scope", "Scope IN ('admin','accountant','client')");
            });
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("AppPermissions");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(120);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("AppRolePermissions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.RoleName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PermissionKey).HasMaxLength(120).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.RoleName);
            entity.HasIndex(x => x.PermissionKey);
            entity.HasIndex(x => new { x.RoleName, x.PermissionKey }).IsUnique();
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("AppUserSessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.UserId).IsRequired();
            entity.Property(x => x.JwtId).IsRequired();
            entity.Property(x => x.RevokedReason).HasMaxLength(200);
            entity.Property(x => x.ClientIp).HasMaxLength(120);
            entity.Property(x => x.UserAgent).HasMaxLength(500);
            entity.HasIndex(x => x.JwtId).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.RevokedAtUtc, x.ExpiresAtUtc });
        });

        modelBuilder.Entity<UserAccessToken>(entity =>
        {
            entity.ToTable("AppUserAccessTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.UserId).IsRequired();
            entity.Property(x => x.Purpose).HasMaxLength(40).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.SessionId);
            entity.Property(x => x.CreatedByUserId);
            entity.Property(x => x.InvalidatedReason).HasMaxLength(200);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.Purpose, x.ExpiresAtUtc });
            entity.HasIndex(x => new { x.SessionId, x.Purpose });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_AppUserAccessTokens_Purpose",
                    "Purpose IN ('invite','password_reset','refresh')");
            });
        });

        modelBuilder.Entity<ComplianceCategory>(entity =>
        {
            entity.ToTable("AppComplianceCategories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
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
            entity.Property(x => x.Id);
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.CategoryId).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.OwnerUserId);
            entity.Property(x => x.RiskLevel).HasMaxLength(20).IsRequired();
            entity.Property(x => x.RequiredDocumentCategory).HasMaxLength(80);
            entity.Property(x => x.LinkedDocumentId);
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
            entity.Property(x => x.Id);
            entity.Property(x => x.ComplianceItemId).IsRequired();
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.RecipientUserId).IsRequired();
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

        modelBuilder.Entity<RequiredDocumentTemplate>(entity =>
        {
            entity.ToTable("AppRequiredDocumentTemplates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.DocumentCategory).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => x.DocumentCategory);
        });

        modelBuilder.Entity<MonthlyPackTemplate>(entity =>
        {
            entity.ToTable("AppMonthlyPackTemplates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.Name).IsUnique();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppMonthlyPackTemplates_AutoCreateDayOfMonth", "AutoCreateDayOfMonth >= 1 AND AutoCreateDayOfMonth <= 28");
            });
        });

        modelBuilder.Entity<MonthlyPackTemplateItem>(entity =>
        {
            entity.ToTable("AppMonthlyPackTemplateItems");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.MonthlyPackTemplateId).IsRequired();
            entity.Property(x => x.RequiredDocumentTemplateId).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.MonthlyPackTemplateId);
            entity.HasIndex(x => x.RequiredDocumentTemplateId);
            entity.HasIndex(x => new { x.MonthlyPackTemplateId, x.RequiredDocumentTemplateId }).IsUnique();
        });

        modelBuilder.Entity<RequestTemplate>(entity =>
        {
            entity.ToTable("AppRequestTemplates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.RequestType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.TitleTemplate).HasMaxLength(300).IsRequired();
            entity.Property(x => x.DescriptionTemplate).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.Name).IsUnique();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppRequestTemplates_Priority", "Priority IN ('low','medium','high','urgent')");
            });
        });

        modelBuilder.Entity<ReminderRule>(entity =>
        {
            entity.ToTable("AppReminderRules");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.TriggerType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.AudienceRole).HasMaxLength(30).IsRequired();
            entity.Property(x => x.MessageTemplate).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.Name).IsUnique();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppReminderRules_AudienceRole", "AudienceRole IN ('admin','accountant','client')");
            });
        });

        modelBuilder.Entity<DeadlineRule>(entity =>
        {
            entity.ToTable("AppDeadlineRules");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.Name).IsUnique();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppDeadlineRules_DueDayOfMonth", "DueDayOfMonth >= 1 AND DueDayOfMonth <= 31");
                table.HasCheckConstraint("CK_AppDeadlineRules_GraceDays", "GraceDays >= 0");
                table.HasCheckConstraint("CK_AppDeadlineRules_Priority", "Priority IN ('low','medium','high','urgent','critical')");
            });
        });

        modelBuilder.Entity<DocumentAccessLog>(entity =>
        {
            entity.ToTable("AppDocumentAccessLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id);
            entity.Property(x => x.DocumentId).IsRequired();
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.AccessedByUserId);
            entity.Property(x => x.AccessedByRole).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(50).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(120);
            entity.Property(x => x.UserAgent).HasMaxLength(500);
            entity.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.AccessedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.DocumentId, x.AccessedAtUtc });
            entity.HasIndex(x => new { x.ClientId, x.AccessedAtUtc });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AppDocumentAccessLogs_AccessedByRole", "AccessedByRole IN ('admin','accountant','client','unknown')");
            });
        });
    }
}




