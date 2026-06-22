using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Infrastructure.Infrastructure.Persistence.MigrationsSqlServer.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class AddUserAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUserAccessTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvalidatedReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUserAccessTokens", x => x.Id);
                    table.CheckConstraint("CK_AppUserAccessTokens_Purpose", "Purpose IN ('invite','password_reset','refresh')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUserAccessTokens_SessionId_Purpose",
                table: "AppUserAccessTokens",
                columns: new[] { "SessionId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_AppUserAccessTokens_TokenHash",
                table: "AppUserAccessTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUserAccessTokens_UserId_Purpose_ExpiresAtUtc",
                table: "AppUserAccessTokens",
                columns: new[] { "UserId", "Purpose", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUserAccessTokens");
        }
    }
}
