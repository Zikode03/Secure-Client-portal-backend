using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Backend.Migrations
{
    public partial class Phase3RequestsCommentsNotifications : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RelatedDocumentId",
                table: "AppRequests",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RequestType",
                table: "AppRequests",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "clarification")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "AppRequests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedByUserId",
                table: "AppRequests",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AppRequests_RelatedDocumentId",
                table: "AppRequests",
                column: "RelatedDocumentId");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppRequests_Status",
                table: "AppRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppRequests_Priority",
                table: "AppRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppRequests_Status",
                table: "AppRequests",
                sql: "Status IN ('open','awaiting_client','awaiting_accountant','resolved')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppRequests_Priority",
                table: "AppRequests",
                sql: "Priority IN ('low','medium','high','urgent')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppRequests_RequestType",
                table: "AppRequests",
                sql: "RequestType IN ('missing_document','reupload','clarification','renewal','signature')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppRequests_RequestType",
                table: "AppRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppRequests_Status",
                table: "AppRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppRequests_Priority",
                table: "AppRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppRequests_Status",
                table: "AppRequests",
                sql: "Status IN ('open','awaiting_client','awaiting_accountant','resolved')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppRequests_Priority",
                table: "AppRequests",
                sql: "Priority IN ('low','medium','high','urgent')");

            migrationBuilder.DropIndex(
                name: "IX_AppRequests_RelatedDocumentId",
                table: "AppRequests");

            migrationBuilder.DropColumn(
                name: "RelatedDocumentId",
                table: "AppRequests");

            migrationBuilder.DropColumn(
                name: "RequestType",
                table: "AppRequests");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "AppRequests");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "AppRequests");
        }
    }
}
