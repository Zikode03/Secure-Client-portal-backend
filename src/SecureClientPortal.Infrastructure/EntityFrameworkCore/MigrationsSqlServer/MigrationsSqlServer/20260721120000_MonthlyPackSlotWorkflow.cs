using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecureClientPortal.Backend.Data;

#nullable disable

namespace SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.MigrationsSqlServer
{
    [DbContext(typeof(PortalDbContext))]
    [Migration("20260721120000_MonthlyPackSlotWorkflow")]
    public partial class MonthlyPackSlotWorkflow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppDocumentSlots_Status",
                table: "AppDocumentSlots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks");

            migrationBuilder.Sql("""
                UPDATE AppDocumentSlots
                SET Status = CASE Status
                    WHEN 'missing' THEN 'not_started'
                    WHEN 'uploaded' THEN 'draft'
                    WHEN 'filed' THEN 'accepted'
                    ELSE Status
                END
                """);

            migrationBuilder.Sql("""
                UPDATE AppMonthlyPacks
                SET Status = CASE Status
                    WHEN 'draft' THEN 'not_started'
                    WHEN 'submitted' THEN 'partially_submitted'
                    WHEN 'completed' THEN 'complete'
                    WHEN 'reopened' THEN 'in_progress'
                    ELSE Status
                END
                """);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "AppDocumentSlots",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "AppDocumentSlots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByUserId",
                table: "AppDocumentSlots",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "AppMonthlyPacks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppDocumentSlots_Status",
                table: "AppDocumentSlots",
                sql: "Status IN ('not_started','draft','submitted','under_review','accepted','rejected','reupload_required','not_applicable')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks",
                sql: "Status IN ('not_started','in_progress','partially_submitted','under_review','complete','closed')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppDocumentSlots_Status",
                table: "AppDocumentSlots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "AppMonthlyPacks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE AppDocumentSlots
                SET Status = CASE Status
                    WHEN 'not_started' THEN 'missing'
                    WHEN 'draft' THEN 'uploaded'
                    WHEN 'submitted' THEN 'uploaded'
                    WHEN 'reupload_required' THEN 'rejected'
                    WHEN 'not_applicable' THEN 'missing'
                    WHEN 'accepted' THEN 'filed'
                    ELSE Status
                END
                """);

            migrationBuilder.Sql("""
                UPDATE AppMonthlyPacks
                SET Status = CASE Status
                    WHEN 'not_started' THEN 'draft'
                    WHEN 'partially_submitted' THEN 'submitted'
                    WHEN 'complete' THEN 'completed'
                    WHEN 'closed' THEN 'reopened'
                    ELSE Status
                END
                """);

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "AppDocumentSlots");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "AppDocumentSlots");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "AppDocumentSlots");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppDocumentSlots_Status",
                table: "AppDocumentSlots",
                sql: "Status IN ('missing','uploaded','under_review','accepted','rejected','filed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppMonthlyPacks_Status",
                table: "AppMonthlyPacks",
                sql: "Status IN ('draft','in_progress','submitted','under_review','completed','reopened')");
        }
    }
}
