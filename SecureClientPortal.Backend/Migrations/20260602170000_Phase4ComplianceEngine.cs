using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Backend.Migrations
{
    public partial class Phase4ComplianceEngine : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "AppComplianceCategories",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "AppComplianceItems",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RiskLevel",
                table: "AppComplianceItems",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "medium")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AppComplianceCategories_Code",
                table: "AppComplianceCategories",
                column: "Code",
                unique: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppComplianceItems_Status",
                table: "AppComplianceItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppComplianceItems_Status",
                table: "AppComplianceItems",
                sql: "Status IN ('missing','pending','valid','expiring_soon','expired','rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppComplianceItems_RiskLevel",
                table: "AppComplianceItems",
                sql: "RiskLevel IN ('low','medium','high','critical')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppComplianceItems_RiskLevel",
                table: "AppComplianceItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppComplianceItems_Status",
                table: "AppComplianceItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppComplianceItems_Status",
                table: "AppComplianceItems",
                sql: "Status IN ('missing','pending','valid','expiring_soon','expired','rejected')");

            migrationBuilder.DropIndex(
                name: "IX_AppComplianceCategories_Code",
                table: "AppComplianceCategories");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "AppComplianceCategories");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "AppComplianceItems");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                table: "AppComplianceItems");
        }
    }
}
