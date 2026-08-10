using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class ClientSelfServiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine",
                table: "AppClients",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "AppClients",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "AppClients",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "AppClients",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "AppClients",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactJobTitle",
                table: "AppClients",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "AppClients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TaxNumber",
                table: "AppClients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TradingName",
                table: "AppClients",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VatNumber",
                table: "AppClients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AppNotificationPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeadlineAlerts = table.Column<bool>(type: "bit", nullable: false),
                    RejectionAlerts = table.Column<bool>(type: "bit", nullable: false),
                    ComplianceAlerts = table.Column<bool>(type: "bit", nullable: false),
                    WeeklySummary = table.Column<bool>(type: "bit", nullable: false),
                    BrowserAlerts = table.Column<bool>(type: "bit", nullable: false),
                    EmailReminders = table.Column<bool>(type: "bit", nullable: false),
                    EscalationAlerts = table.Column<bool>(type: "bit", nullable: false),
                    QuietHours = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppNotificationPreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AppNotificationPreferences_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppNotificationPreferences");

            migrationBuilder.DropColumn(
                name: "AddressLine",
                table: "AppClients");

            migrationBuilder.DropColumn(
                name: "City",
                table: "AppClients");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "AppClients");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "AppClients");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "AppClients");

            migrationBuilder.DropColumn(
                name: "PrimaryContactJobTitle",
                table: "AppClients");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "AppClients");

            migrationBuilder.DropColumn(
                name: "TaxNumber",
                table: "AppClients");

            migrationBuilder.DropColumn(
                name: "TradingName",
                table: "AppClients");

            migrationBuilder.DropColumn(
                name: "VatNumber",
                table: "AppClients");
        }
    }
}
