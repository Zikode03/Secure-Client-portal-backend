using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Backend.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class ImplementFirmManagementAndAccessControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppDeadlineRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DueDayOfMonth = table.Column<int>(type: "int", nullable: false),
                    GraceDays = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDeadlineRules", x => x.Id);
                    table.CheckConstraint("CK_AppDeadlineRules_DueDayOfMonth", "DueDayOfMonth >= 1 AND DueDayOfMonth <= 31");
                    table.CheckConstraint("CK_AppDeadlineRules_GraceDays", "GraceDays >= 0");
                    table.CheckConstraint("CK_AppDeadlineRules_Priority", "Priority IN ('low','medium','high','urgent','critical')");
                });

            migrationBuilder.CreateTable(
                name: "AppDocumentAccessLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccessedByUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccessedByRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AccessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDocumentAccessLogs", x => x.Id);
                    table.CheckConstraint("CK_AppDocumentAccessLogs_AccessedByRole", "AccessedByRole IN ('admin','accountant','client','unknown')");
                });

            migrationBuilder.CreateTable(
                name: "AppMonthlyPackTemplateItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MonthlyPackTemplateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequiredDocumentTemplateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMonthlyPackTemplateItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppMonthlyPackTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AutoCreateDayOfMonth = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMonthlyPackTemplates", x => x.Id);
                    table.CheckConstraint("CK_AppMonthlyPackTemplates_AutoCreateDayOfMonth", "AutoCreateDayOfMonth >= 1 AND AutoCreateDayOfMonth <= 28");
                });

            migrationBuilder.CreateTable(
                name: "AppPermissions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystemPermission = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPermissions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AppReminderRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TriggerType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DaysBeforeDue = table.Column<int>(type: "int", nullable: false),
                    AudienceRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MessageTemplate = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppReminderRules", x => x.Id);
                    table.CheckConstraint("CK_AppReminderRules_AudienceRole", "AudienceRole IN ('admin','accountant','client')");
                });

            migrationBuilder.CreateTable(
                name: "AppRequestTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RequestType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TitleTemplate = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DescriptionTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultDueInDays = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRequestTemplates", x => x.Id);
                    table.CheckConstraint("CK_AppRequestTemplates_Priority", "Priority IN ('low','medium','high','urgent')");
                });

            migrationBuilder.CreateTable(
                name: "AppRequiredDocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DocumentCategory = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DefaultDueDayOfMonth = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRequiredDocumentTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppRolePermissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRolePermissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDeadlineRules_Name",
                table: "AppDeadlineRules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppDocumentAccessLogs_ClientId_AccessedAtUtc",
                table: "AppDocumentAccessLogs",
                columns: new[] { "ClientId", "AccessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppDocumentAccessLogs_DocumentId_AccessedAtUtc",
                table: "AppDocumentAccessLogs",
                columns: new[] { "DocumentId", "AccessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppMonthlyPackTemplateItems_MonthlyPackTemplateId",
                table: "AppMonthlyPackTemplateItems",
                column: "MonthlyPackTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AppMonthlyPackTemplateItems_MonthlyPackTemplateId_RequiredDocumentTemplateId",
                table: "AppMonthlyPackTemplateItems",
                columns: new[] { "MonthlyPackTemplateId", "RequiredDocumentTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppMonthlyPackTemplateItems_RequiredDocumentTemplateId",
                table: "AppMonthlyPackTemplateItems",
                column: "RequiredDocumentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AppMonthlyPackTemplates_Name",
                table: "AppMonthlyPackTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppReminderRules_Name",
                table: "AppReminderRules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRequestTemplates_Name",
                table: "AppRequestTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRequiredDocumentTemplates_DocumentCategory",
                table: "AppRequiredDocumentTemplates",
                column: "DocumentCategory");

            migrationBuilder.CreateIndex(
                name: "IX_AppRequiredDocumentTemplates_Name",
                table: "AppRequiredDocumentTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRolePermissions_PermissionKey",
                table: "AppRolePermissions",
                column: "PermissionKey");

            migrationBuilder.CreateIndex(
                name: "IX_AppRolePermissions_RoleName",
                table: "AppRolePermissions",
                column: "RoleName");

            migrationBuilder.CreateIndex(
                name: "IX_AppRolePermissions_RoleName_PermissionKey",
                table: "AppRolePermissions",
                columns: new[] { "RoleName", "PermissionKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppDeadlineRules");

            migrationBuilder.DropTable(
                name: "AppDocumentAccessLogs");

            migrationBuilder.DropTable(
                name: "AppMonthlyPackTemplateItems");

            migrationBuilder.DropTable(
                name: "AppMonthlyPackTemplates");

            migrationBuilder.DropTable(
                name: "AppPermissions");

            migrationBuilder.DropTable(
                name: "AppReminderRules");

            migrationBuilder.DropTable(
                name: "AppRequestTemplates");

            migrationBuilder.DropTable(
                name: "AppRequiredDocumentTemplates");

            migrationBuilder.DropTable(
                name: "AppRolePermissions");
        }
    }
}
