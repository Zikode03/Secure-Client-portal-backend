using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Backend.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class AddRoleSessionsAndClientStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppClients_Status",
                table: "AppClients");

            migrationBuilder.CreateTable(
                name: "AppRoles",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PermissionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSystemRole = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRoles", x => x.Name);
                    table.CheckConstraint("CK_AppRoles_Scope", "Scope IN ('admin','accountant','client')");
                });

            migrationBuilder.CreateTable(
                name: "AppUserSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JwtId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientIp = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUserSessions", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppClients_Status",
                table: "AppClients",
                sql: "Status IN ('active','inactive')");

            migrationBuilder.CreateIndex(
                name: "IX_AppRoles_DisplayName",
                table: "AppRoles",
                column: "DisplayName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUserSessions_JwtId",
                table: "AppUserSessions",
                column: "JwtId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUserSessions_UserId_RevokedAtUtc_ExpiresAtUtc",
                table: "AppUserSessions",
                columns: new[] { "UserId", "RevokedAtUtc", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppRoles");

            migrationBuilder.DropTable(
                name: "AppUserSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppClients_Status",
                table: "AppClients");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppClients_Status",
                table: "AppClients",
                sql: "Status IN ('pending','active','at_risk','archived')");
        }
    }
}
