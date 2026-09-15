using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class ComplianceObligationApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppComplianceAutomationConfigurations",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComplianceAutomationConfigurations", x => x.Key);
                    table.ForeignKey(
                        name: "FK_AppComplianceAutomationConfigurations_AppClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "AppClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppComplianceObligations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplianceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RuleJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StateJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComplianceObligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppComplianceObligations_AppClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "AppClients",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AppComplianceObligations_AppComplianceItems_ComplianceItemId",
                        column: x => x.ComplianceItemId,
                        principalTable: "AppComplianceItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceAutomationConfigurations_ClientId",
                table: "AppComplianceAutomationConfigurations",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceObligations_ClientId_Code_PeriodStartUtc",
                table: "AppComplianceObligations",
                columns: new[] { "ClientId", "Code", "PeriodStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceObligations_ComplianceItemId",
                table: "AppComplianceObligations",
                column: "ComplianceItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppComplianceAutomationConfigurations");

            migrationBuilder.DropTable(
                name: "AppComplianceObligations");
        }
    }
}
