using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Backend.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAuditLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAuditLogs", x => x.Id);
                    table.CheckConstraint("CK_AppAuditLogs_ActorRole", "ActorRole IN ('admin','accountant','client','unknown')");
                });

            migrationBuilder.CreateTable(
                name: "AppClientAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountantUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppClientAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppClients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ComplianceHealth = table.Column<int>(type: "int", nullable: false),
                    AssignedAccountantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PrimaryContact = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppClients", x => x.Id);
                    table.CheckConstraint("CK_AppClients_ComplianceHealth", "ComplianceHealth >= 0 AND ComplianceHealth <= 100");
                    table.CheckConstraint("CK_AppClients_Status", "Status IN ('pending','active','at_risk','archived')");
                });

            migrationBuilder.CreateTable(
                name: "AppComplianceCategories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComplianceCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppComplianceItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RiskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequiredDocumentCategory = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    LinkedDocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiryDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComplianceItems", x => x.Id);
                    table.CheckConstraint("CK_AppComplianceItems_RiskLevel", "RiskLevel IN ('low','medium','high','critical')");
                    table.CheckConstraint("CK_AppComplianceItems_Status", "Status IN ('missing','pending','valid','expiring_soon','expired','rejected')");
                });

            migrationBuilder.CreateTable(
                name: "AppComplianceReminders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ComplianceItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecipientUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComplianceReminders", x => x.Id);
                    table.CheckConstraint("CK_AppComplianceReminders_Status", "Status IN ('pending','sent','dismissed')");
                });

            migrationBuilder.CreateTable(
                name: "AppDocumentComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AuthorUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AuthorRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDocumentComments", x => x.Id);
                    table.CheckConstraint("CK_AppDocumentComments_AuthorRole", "AuthorRole IN ('admin','accountant','client')");
                });

            migrationBuilder.CreateTable(
                name: "AppDocuments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DocumentSlotId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CurrentVersionNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    IsFiled = table.Column<bool>(type: "bit", nullable: false),
                    FiledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FiledByUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDocuments", x => x.Id);
                    table.CheckConstraint("CK_AppDocuments_Status", "Status IN ('draft','pending','under_review','accepted','rejected','filed')");
                });

            migrationBuilder.CreateTable(
                name: "AppDocumentSlots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MonthlyPackId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CurrentDocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDocumentSlots", x => x.Id);
                    table.CheckConstraint("CK_AppDocumentSlots_Status", "Status IN ('missing','uploaded','under_review','accepted','rejected','filed')");
                });

            migrationBuilder.CreateTable(
                name: "AppDocumentVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDocumentVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppFilingRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(280)", maxLength: 280, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppFilingRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppMonthlyPacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMonthlyPacks", x => x.Id);
                    table.CheckConstraint("CK_AppMonthlyPacks_Month", "Month >= 1 AND Month <= 12");
                    table.CheckConstraint("CK_AppMonthlyPacks_Status", "Status IN ('draft','in_progress','submitted','under_review','completed')");
                });

            migrationBuilder.CreateTable(
                name: "AppNotifications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    LinkUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppRequestComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AuthorUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AuthorRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRequestComments", x => x.Id);
                    table.CheckConstraint("CK_AppRequestComments_AuthorRole", "AuthorRole IN ('admin','accountant','client')");
                });

            migrationBuilder.CreateTable(
                name: "AppRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RelatedDocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRequests", x => x.Id);
                    table.CheckConstraint("CK_AppRequests_Priority", "Priority IN ('low','medium','high','urgent')");
                    table.CheckConstraint("CK_AppRequests_RequestType", "RequestType IN ('missing_document','reupload','clarification','renewal','signature')");
                    table.CheckConstraint("CK_AppRequests_Status", "Status IN ('open','awaiting_client','awaiting_accountant','resolved')");
                });

            migrationBuilder.CreateTable(
                name: "AppReviewDecisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReviewerUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReviewerRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppReviewDecisions", x => x.Id);
                    table.CheckConstraint("CK_AppReviewDecisions_Decision", "Decision IN ('under_review','accepted','rejected','request_reupload')");
                    table.CheckConstraint("CK_AppReviewDecisions_ReviewerRole", "ReviewerRole IN ('admin','accountant')");
                });

            migrationBuilder.CreateTable(
                name: "AppSystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ValueJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSystemSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AppTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTasks", x => x.Id);
                    table.CheckConstraint("CK_AppTasks_Priority", "Priority IN ('low','medium','high','urgent')");
                    table.CheckConstraint("CK_AppTasks_Status", "Status IN ('todo','in_progress','blocked','done')");
                });

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClientIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProfileJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                    table.CheckConstraint("CK_AppUsers_Role", "Role IN ('admin','accountant','client')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAuditLogs_ClientId",
                table: "AppAuditLogs",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAuditLogs_CreatedAtUtc",
                table: "AppAuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppAuditLogs_EntityType_EntityId",
                table: "AppAuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppClientAssignments_AccountantUserId",
                table: "AppClientAssignments",
                column: "AccountantUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppClientAssignments_AccountantUserId_ClientId",
                table: "AppClientAssignments",
                columns: new[] { "AccountantUserId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppClientAssignments_ClientId",
                table: "AppClientAssignments",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AppClients_AssignedAccountantId",
                table: "AppClients",
                column: "AssignedAccountantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppClients_Status",
                table: "AppClients",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceCategories_Code",
                table: "AppComplianceCategories",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceCategories_Name",
                table: "AppComplianceCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceItems_ClientId_CategoryId",
                table: "AppComplianceItems",
                columns: new[] { "ClientId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceItems_ClientId_Status",
                table: "AppComplianceItems",
                columns: new[] { "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceReminders_ClientId_ScheduledForUtc",
                table: "AppComplianceReminders",
                columns: new[] { "ClientId", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceReminders_RecipientUserId_Status",
                table: "AppComplianceReminders",
                columns: new[] { "RecipientUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppDocumentComments_DocumentId_CreatedAtUtc",
                table: "AppDocumentComments",
                columns: new[] { "DocumentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppDocuments_ClientId_IsFiled",
                table: "AppDocuments",
                columns: new[] { "ClientId", "IsFiled" });

            migrationBuilder.CreateIndex(
                name: "IX_AppDocuments_ClientId_Status",
                table: "AppDocuments",
                columns: new[] { "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppDocuments_UploadedAtUtc",
                table: "AppDocuments",
                column: "UploadedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppDocumentSlots_MonthlyPackId",
                table: "AppDocumentSlots",
                column: "MonthlyPackId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDocumentSlots_MonthlyPackId_Category",
                table: "AppDocumentSlots",
                columns: new[] { "MonthlyPackId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppDocumentVersions_DocumentId",
                table: "AppDocumentVersions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDocumentVersions_DocumentId_VersionNumber",
                table: "AppDocumentVersions",
                columns: new[] { "DocumentId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppFilingRules_Category",
                table: "AppFilingRules",
                column: "Category",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppMonthlyPacks_ClientId_Year_Month",
                table: "AppMonthlyPacks",
                columns: new[] { "ClientId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppNotifications_ClientId",
                table: "AppNotifications",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AppNotifications_UserId_IsRead_CreatedAtUtc",
                table: "AppNotifications",
                columns: new[] { "UserId", "IsRead", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRequestComments_RequestId_CreatedAtUtc",
                table: "AppRequestComments",
                columns: new[] { "RequestId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRequests_ClientId_Status",
                table: "AppRequests",
                columns: new[] { "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRequests_DueDateUtc",
                table: "AppRequests",
                column: "DueDateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppRequests_RelatedDocumentId",
                table: "AppRequests",
                column: "RelatedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppReviewDecisions_DecidedAtUtc",
                table: "AppReviewDecisions",
                column: "DecidedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppReviewDecisions_DocumentId",
                table: "AppReviewDecisions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTasks_ClientId_Status",
                table: "AppTasks",
                columns: new[] { "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTasks_DueDateUtc",
                table: "AppTasks",
                column: "DueDateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Email",
                table: "AppUsers",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAuditLogs");

            migrationBuilder.DropTable(
                name: "AppClientAssignments");

            migrationBuilder.DropTable(
                name: "AppClients");

            migrationBuilder.DropTable(
                name: "AppComplianceCategories");

            migrationBuilder.DropTable(
                name: "AppComplianceItems");

            migrationBuilder.DropTable(
                name: "AppComplianceReminders");

            migrationBuilder.DropTable(
                name: "AppDocumentComments");

            migrationBuilder.DropTable(
                name: "AppDocuments");

            migrationBuilder.DropTable(
                name: "AppDocumentSlots");

            migrationBuilder.DropTable(
                name: "AppDocumentVersions");

            migrationBuilder.DropTable(
                name: "AppFilingRules");

            migrationBuilder.DropTable(
                name: "AppMonthlyPacks");

            migrationBuilder.DropTable(
                name: "AppNotifications");

            migrationBuilder.DropTable(
                name: "AppRequestComments");

            migrationBuilder.DropTable(
                name: "AppRequests");

            migrationBuilder.DropTable(
                name: "AppReviewDecisions");

            migrationBuilder.DropTable(
                name: "AppSystemSettings");

            migrationBuilder.DropTable(
                name: "AppTasks");

            migrationBuilder.DropTable(
                name: "AppUsers");
        }
    }
}
