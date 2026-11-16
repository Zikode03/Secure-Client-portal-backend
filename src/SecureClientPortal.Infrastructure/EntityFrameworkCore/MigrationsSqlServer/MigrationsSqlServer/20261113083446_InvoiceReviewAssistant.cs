using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class InvoiceReviewAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentVersionNumber",
                table: "AppReviewDecisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceReviewRevision",
                table: "AppReviewDecisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InvoiceReviewRequired",
                table: "AppDocuments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceReviewRevision",
                table: "AppDocuments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AppInvoiceReviewSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RuleSetVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    InputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FindingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppInvoiceReviewSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppInvoiceReviewSnapshots_AppDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "AppDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppInvoiceReviewSnapshots_DocumentId_RequestId",
                table: "AppInvoiceReviewSnapshots",
                columns: new[] { "DocumentId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppInvoiceReviewSnapshots_DocumentId_Revision",
                table: "AppInvoiceReviewSnapshots",
                columns: new[] { "DocumentId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppInvoiceReviewSnapshots");

            migrationBuilder.DropColumn(
                name: "DocumentVersionNumber",
                table: "AppReviewDecisions");

            migrationBuilder.DropColumn(
                name: "InvoiceReviewRevision",
                table: "AppReviewDecisions");

            migrationBuilder.DropColumn(
                name: "InvoiceReviewRequired",
                table: "AppDocuments");

            migrationBuilder.DropColumn(
                name: "InvoiceReviewRevision",
                table: "AppDocuments");
        }
    }
}
