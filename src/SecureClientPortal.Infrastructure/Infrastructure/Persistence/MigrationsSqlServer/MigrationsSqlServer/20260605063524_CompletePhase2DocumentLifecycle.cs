using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Backend.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class CompletePhase2DocumentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppDocuments_Status",
                table: "AppDocuments");

            migrationBuilder.AddColumn<string>(
                name: "InternalNote",
                table: "AppReviewDecisions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "AppMonthlyPacks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileType",
                table: "AppDocumentVersions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrentVersion",
                table: "AppDocumentVersions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "AppDocumentVersions",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StoredFileName",
                table: "AppDocumentVersions",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FileType",
                table: "AppDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MonthlyPackId",
                table: "AppDocuments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks",
                sql: "Status IN ('draft','in_progress','submitted','under_review','completed','reopened')");

            migrationBuilder.CreateIndex(
                name: "IX_AppDocumentVersions_DocumentId_IsCurrentVersion",
                table: "AppDocumentVersions",
                columns: new[] { "DocumentId", "IsCurrentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_AppDocuments_MonthlyPackId",
                table: "AppDocuments",
                column: "MonthlyPackId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppDocuments_Status",
                table: "AppDocuments",
                sql: "Status IN ('draft','uploaded','under_review','accepted','rejected','filed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks");

            migrationBuilder.DropIndex(
                name: "IX_AppDocumentVersions_DocumentId_IsCurrentVersion",
                table: "AppDocumentVersions");

            migrationBuilder.DropIndex(
                name: "IX_AppDocuments_MonthlyPackId",
                table: "AppDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppDocuments_Status",
                table: "AppDocuments");

            migrationBuilder.DropColumn(
                name: "InternalNote",
                table: "AppReviewDecisions");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "AppMonthlyPacks");

            migrationBuilder.DropColumn(
                name: "FileType",
                table: "AppDocumentVersions");

            migrationBuilder.DropColumn(
                name: "IsCurrentVersion",
                table: "AppDocumentVersions");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "AppDocumentVersions");

            migrationBuilder.DropColumn(
                name: "StoredFileName",
                table: "AppDocumentVersions");

            migrationBuilder.DropColumn(
                name: "FileType",
                table: "AppDocuments");

            migrationBuilder.DropColumn(
                name: "MonthlyPackId",
                table: "AppDocuments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks",
                sql: "Status IN ('draft','in_progress','submitted','under_review','completed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppDocuments_Status",
                table: "AppDocuments",
                sql: "Status IN ('draft','pending','under_review','accepted','rejected','filed')");
        }
    }
}
