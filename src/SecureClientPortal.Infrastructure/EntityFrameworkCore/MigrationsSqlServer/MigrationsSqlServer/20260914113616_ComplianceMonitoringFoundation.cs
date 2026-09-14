using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class ComplianceMonitoringFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppComplianceMonitoringProfiles",
                columns: table => new
                {
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CsdSupplierNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComplianceMonitoringProfiles", x => x.ClientId);
                    table.ForeignKey(
                        name: "FK_AppComplianceMonitoringProfiles_AppClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "AppClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppComplianceVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewAfterUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdentifierFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComplianceVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppComplianceVerifications_AppClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "AppClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppComplianceCheckSettings",
                columns: table => new
                {
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Applicability = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComplianceCheckSettings", x => new { x.ClientId, x.CheckCode });
                    table.ForeignKey(
                        name: "FK_AppComplianceCheckSettings_AppComplianceMonitoringProfiles_ClientId",
                        column: x => x.ClientId,
                        principalTable: "AppComplianceMonitoringProfiles",
                        principalColumn: "ClientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceVerifications_ClientId_CheckCode_RecordedAtUtc",
                table: "AppComplianceVerifications",
                columns: new[] { "ClientId", "CheckCode", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppComplianceCheckSettings");

            migrationBuilder.DropTable(
                name: "AppComplianceVerifications");

            migrationBuilder.DropTable(
                name: "AppComplianceMonitoringProfiles");
        }
    }
}
