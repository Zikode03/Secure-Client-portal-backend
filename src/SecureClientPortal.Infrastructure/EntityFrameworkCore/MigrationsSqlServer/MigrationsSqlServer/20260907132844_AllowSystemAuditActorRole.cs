using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class AllowSystemAuditActorRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppAuditLogs_ActorRole",
                table: "AppAuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppAuditLogs_ActorRole",
                table: "AppAuditLogs",
                sql: "ActorRole IN ('admin','accountant','client','system','unknown')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppAuditLogs_ActorRole",
                table: "AppAuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppAuditLogs_ActorRole",
                table: "AppAuditLogs",
                sql: "ActorRole IN ('admin','accountant','client','unknown')");
        }
    }
}
