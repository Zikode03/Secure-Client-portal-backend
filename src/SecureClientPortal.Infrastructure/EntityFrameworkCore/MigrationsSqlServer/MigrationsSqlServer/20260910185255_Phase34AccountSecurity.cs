using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class Phase34AccountSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Retire outstanding long-lived setup links when this hardening is deployed.
            migrationBuilder.Sql("""
                UPDATE AppUserAccessTokens
                SET ExpiresAtUtc = DATEADD(hour, 24, CreatedAtUtc)
                WHERE Purpose = 'invite' AND ConsumedAtUtc IS NULL AND InvalidatedAtUtc IS NULL
                  AND ExpiresAtUtc > DATEADD(hour, 24, CreatedAtUtc);
                UPDATE AppUserAccessTokens
                SET ExpiresAtUtc = DATEADD(minute, 30, CreatedAtUtc)
                WHERE Purpose = 'password_reset' AND ConsumedAtUtc IS NULL AND InvalidatedAtUtc IS NULL
                  AND ExpiresAtUtc > DATEADD(minute, 30, CreatedAtUtc);
                """);
            migrationBuilder.AddColumn<bool>(
                name: "MfaVerified",
                table: "AppUserSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AppAccountSecurity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastResetRequestUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SmtpProbeHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SmtpProbeExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SmtpVerifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MfaSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PendingSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChallengeHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChallengeExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Persistent = table.Column<bool>(type: "bit", nullable: false),
                    LastTotpStep = table.Column<long>(type: "bigint", nullable: false),
                    RecoveryHashesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAccountSecurity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAccountSecurity_AppUsers_Id",
                        column: x => x.Id,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAccountSecurity");

            migrationBuilder.DropColumn(
                name: "MfaVerified",
                table: "AppUserSessions");
        }
    }
}
