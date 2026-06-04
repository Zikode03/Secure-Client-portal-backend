using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Backend.Migrations
{
    public partial class Phase2DocumentLifecycleFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRequired",
                table: "AppDocumentSlots",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks",
                sql: "Status IN ('draft','in_progress','submitted','under_review','completed')");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppReviewDecisions_Decision",
                table: "AppReviewDecisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppReviewDecisions_Decision",
                table: "AppReviewDecisions",
                sql: "Decision IN ('under_review','accepted','rejected','request_reupload')");

            migrationBuilder.Sql("UPDATE AppMonthlyPacks SET Status = 'draft' WHERE Status = 'open';");
            migrationBuilder.Sql("UPDATE AppMonthlyPacks SET Status = 'submitted' WHERE Status = 'ready_for_review';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks",
                sql: "Status IN ('open','in_progress','ready_for_review','completed')");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppReviewDecisions_Decision",
                table: "AppReviewDecisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppReviewDecisions_Decision",
                table: "AppReviewDecisions",
                sql: "Decision IN ('accepted','rejected')");

            migrationBuilder.DropColumn(
                name: "IsRequired",
                table: "AppDocumentSlots");

            migrationBuilder.Sql("UPDATE AppMonthlyPacks SET Status = 'open' WHERE Status = 'draft';");
            migrationBuilder.Sql("UPDATE AppMonthlyPacks SET Status = 'ready_for_review' WHERE Status = 'submitted';");
        }
    }
}
