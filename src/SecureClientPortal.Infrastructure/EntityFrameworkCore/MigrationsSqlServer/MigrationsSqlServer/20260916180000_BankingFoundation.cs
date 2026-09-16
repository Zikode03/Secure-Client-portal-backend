using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecureClientPortal.Backend.Data;

#nullable disable

namespace SecureClientPortal.Infrastructure.EntityFrameworkCore.MigrationsSqlServer.MigrationsSqlServer;

[DbContext(typeof(BankingDbContext))]
[Migration("20260916180000_BankingFoundation")]
public partial class BankingFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppBankConnections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Provider = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                ExternalConnectionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                ConnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                LastSyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ConsentExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DisconnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppBankConnections", x => x.Id);
                table.CheckConstraint("CK_AppBankConnections_Status", "Status IN ('connecting','connected','needs_attention','consent_expiring','sync_failed','disconnected')");
                table.ForeignKey(
                    name: "FK_AppBankConnections_AppClients_ClientId",
                    column: x => x.ClientId,
                    principalTable: "AppClients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "AppBankAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BankConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ExternalAccountId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                BankName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                AccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                AccountType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                AccountNumberMasked = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                CurrentBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                AvailableBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                LastUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppBankAccounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppBankAccounts_AppBankConnections_BankConnectionId",
                    column: x => x.BankConnectionId,
                    principalTable: "AppBankConnections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AppBankAccounts_AppClients_ClientId",
                    column: x => x.ClientId,
                    principalTable: "AppClients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "AppBankConsentRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BankConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Provider = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                Scope = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                GrantedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppBankConsentRecords", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppBankConsentRecords_AppBankConnections_BankConnectionId",
                    column: x => x.BankConnectionId,
                    principalTable: "AppBankConnections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AppBankConsentRecords_AppClients_ClientId",
                    column: x => x.ClientId,
                    principalTable: "AppClients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "AppBankSyncRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BankConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Provider = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                TransactionsReceived = table.Column<int>(type: "int", nullable: false),
                FromDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ToDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppBankSyncRuns", x => x.Id);
                table.CheckConstraint("CK_AppBankSyncRuns_Status", "Status IN ('running','completed','failed')");
                table.ForeignKey(
                    name: "FK_AppBankSyncRuns_AppBankConnections_BankConnectionId",
                    column: x => x.BankConnectionId,
                    principalTable: "AppBankConnections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AppBankSyncRuns_AppClients_ClientId",
                    column: x => x.ClientId,
                    principalTable: "AppClients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "AppBankTransactions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ExternalTransactionId = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                TransactionDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                PostedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                ProviderCategory = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                ImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppBankTransactions", x => x.Id);
                table.CheckConstraint("CK_AppBankTransactions_Direction", "Direction IN ('debit','credit')");
                table.ForeignKey(
                    name: "FK_AppBankTransactions_AppBankAccounts_BankAccountId",
                    column: x => x.BankAccountId,
                    principalTable: "AppBankAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AppBankTransactions_AppClients_ClientId",
                    column: x => x.ClientId,
                    principalTable: "AppClients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(name: "IX_AppBankConnections_ClientId_Status", table: "AppBankConnections", columns: new[] { "ClientId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_AppBankConnections_Provider_ExternalConnectionId", table: "AppBankConnections", columns: new[] { "Provider", "ExternalConnectionId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_AppBankAccounts_BankConnectionId_ExternalAccountId", table: "AppBankAccounts", columns: new[] { "BankConnectionId", "ExternalAccountId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_AppBankAccounts_ClientId", table: "AppBankAccounts", column: "ClientId");
        migrationBuilder.CreateIndex(name: "IX_AppBankConsentRecords_BankConnectionId", table: "AppBankConsentRecords", column: "BankConnectionId");
        migrationBuilder.CreateIndex(name: "IX_AppBankConsentRecords_ClientId_GrantedAtUtc", table: "AppBankConsentRecords", columns: new[] { "ClientId", "GrantedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_AppBankSyncRuns_BankConnectionId_StartedAtUtc", table: "AppBankSyncRuns", columns: new[] { "BankConnectionId", "StartedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_AppBankSyncRuns_ClientId_StartedAtUtc", table: "AppBankSyncRuns", columns: new[] { "ClientId", "StartedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_AppBankTransactions_BankAccountId_ExternalTransactionId", table: "AppBankTransactions", columns: new[] { "BankAccountId", "ExternalTransactionId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_AppBankTransactions_ClientId_TransactionDateUtc", table: "AppBankTransactions", columns: new[] { "ClientId", "TransactionDateUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AppBankConsentRecords");
        migrationBuilder.DropTable(name: "AppBankSyncRuns");
        migrationBuilder.DropTable(name: "AppBankTransactions");
        migrationBuilder.DropTable(name: "AppBankAccounts");
        migrationBuilder.DropTable(name: "AppBankConnections");
    }
}
