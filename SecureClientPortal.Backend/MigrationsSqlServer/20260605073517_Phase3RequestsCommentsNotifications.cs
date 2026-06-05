using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Backend.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class Phase3RequestsCommentsNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppRequests_RequestType",
                table: "AppRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppRequests_Status",
                table: "AppRequests");

            migrationBuilder.Sql(
                """
                UPDATE AppRequests
                SET RequestType = CASE RequestType
                    WHEN 'reupload' THEN 'reupload_required'
                    WHEN 'clarification' THEN 'clarification_needed'
                    WHEN 'signature' THEN 'signature_required'
                    WHEN 'renewal' THEN 'compliance_renewal'
                    ELSE RequestType
                END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE AppRequests
                SET Status = CASE
                    WHEN Status = 'awaiting_client' THEN 'waiting_on_client'
                    WHEN Status = 'awaiting_accountant' THEN 'waiting_on_accountant'
                    WHEN Status <> 'resolved' AND DueDateUtc IS NOT NULL AND DueDateUtc < SYSUTCDATETIME() THEN 'overdue'
                    ELSE Status
                END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppRequests_RequestType",
                table: "AppRequests",
                sql: "RequestType IN ('missing_document','reupload_required','clarification_needed','signature_required','compliance_renewal')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppRequests_Status",
                table: "AppRequests",
                sql: "Status IN ('open','waiting_on_client','waiting_on_accountant','resolved','overdue')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppRequests_RequestType",
                table: "AppRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppRequests_Status",
                table: "AppRequests");

            migrationBuilder.Sql(
                """
                UPDATE AppRequests
                SET RequestType = CASE RequestType
                    WHEN 'reupload_required' THEN 'reupload'
                    WHEN 'clarification_needed' THEN 'clarification'
                    WHEN 'signature_required' THEN 'signature'
                    WHEN 'compliance_renewal' THEN 'renewal'
                    ELSE RequestType
                END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE AppRequests
                SET Status = CASE
                    WHEN Status = 'waiting_on_client' THEN 'awaiting_client'
                    WHEN Status = 'waiting_on_accountant' THEN 'awaiting_accountant'
                    WHEN Status = 'overdue' THEN 'awaiting_client'
                    ELSE Status
                END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppRequests_RequestType",
                table: "AppRequests",
                sql: "RequestType IN ('missing_document','reupload','clarification','renewal','signature')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppRequests_Status",
                table: "AppRequests",
                sql: "Status IN ('open','awaiting_client','awaiting_accountant','resolved')");
        }
    }
}
