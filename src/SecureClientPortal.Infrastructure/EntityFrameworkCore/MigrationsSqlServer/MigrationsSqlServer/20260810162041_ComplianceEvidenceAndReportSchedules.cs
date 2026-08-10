using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class ComplianceEvidenceAndReportSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppComplianceEvidenceVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplianceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsCurrentVersion = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComplianceEvidenceVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppComplianceEvidenceVersions_AppComplianceItems_ComplianceItemId",
                        column: x => x.ComplianceItemId,
                        principalTable: "AppComplianceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppReportSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReportType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecipientsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NextRunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastScheduledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppReportSchedules", x => x.Id);
                    table.CheckConstraint("CK_AppReportSchedules_Frequency", "Frequency IN ('weekly','monthly')");
                    table.CheckConstraint("CK_AppReportSchedules_ReportType", "ReportType IN ('compliance')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceEvidenceVersions_ClientId_UploadedAtUtc",
                table: "AppComplianceEvidenceVersions",
                columns: new[] { "ClientId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceEvidenceVersions_ComplianceItemId_VersionNumber",
                table: "AppComplianceEvidenceVersions",
                columns: new[] { "ComplianceItemId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppReportSchedules_ClientId_NextRunAtUtc",
                table: "AppReportSchedules",
                columns: new[] { "ClientId", "NextRunAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppReportSchedules_CreatedByUserId_NextRunAtUtc",
                table: "AppReportSchedules",
                columns: new[] { "CreatedByUserId", "NextRunAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppComplianceEvidenceVersions");

            migrationBuilder.DropTable(
                name: "AppReportSchedules");
        }
    }
}
