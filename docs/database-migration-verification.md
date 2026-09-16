# Development database migration verification

Verified on 2026-09-16, backend `main`, after pulling latest main at `48837b4`.
Target: SQL Server `localhost,1433`, database `secure_client_portal_dev`.
No production database was modified. No database was deleted, reset, or restored.
No FNB or other feature development was performed.

## PORTAL DB CONTEXT

Migrations in source (also discovered by EF):

- `20260623183110_InitialGuidSchema`
- `20260721120000_MonthlyPackSlotWorkflow`
- `20260722130000_RequestInternalComments`
- `20260810155443_ClientSelfServiceSettings`
- `20260810162041_ComplianceEvidenceAndReportSchedules`
- `20260907132844_AllowSystemAuditActorRole`
- `20260910070000_RequestReadStates`
- `20260910185255_Phase34AccountSecurity`
- `20260914113616_ComplianceMonitoringFoundation`
- `20260914180809_ComplianceObligationApi`

Migrations applied: all ten above, verified through EF and SQL history.

Pending: none. `database update` applied no migrations.

Pending model changes: none; EF Core 8.0.27 detection returned exit code 0.

Database objects verified:

- All nine Compliance tables: `AppComplianceAutomationConfigurations`,
  `AppComplianceCategories`, `AppComplianceCheckSettings`,
  `AppComplianceEvidenceVersions`, `AppComplianceItems`,
  `AppComplianceMonitoringProfiles`, `AppComplianceObligations`,
  `AppComplianceReminders`, and `AppComplianceVerifications`.
- `ComplianceVerification` physically maps to `dbo.AppComplianceVerifications`.
- All columns, SQL types, lengths, precision, nullability, primary keys,
  secondary indexes, and six foreign keys introduced by the two September 14
  migrations were checked in SQL Server catalogs. Foreign keys are enabled and trusted.
- Both unique obligation indexes and the verification composite index exist.

Action taken: retained the existing Portal migration chain and snapshot; no new
Portal migration was generated. Runtime and design-time configuration now
explicitly agree on migration assembly and history table.

## BANKING DB CONTEXT

Migrations in source (also discovered by EF):
`20260916180000_BankingFoundation`, exactly one migration.

Migrations applied: the same foundation migration, ProductVersion `8.0.27`,
verified in the dedicated history table and through EF.

Pending: none. `database update` applied no migrations.

Model snapshot: legitimate EF Core-generated
`src/SecureClientPortal.Infrastructure/EntityFrameworkCore/MigrationsSqlServer/Banking/BankingDbContextModelSnapshot.cs`.
Its generated contents were not manually edited. SHA256:
`CAA00B64CE45AEA2E57793746B2F0FC809A72DF8320B1AA98635E029E4BD3715`.

Pending model changes: none; EF Core 8.0.27 detection returned exit code 0.

Database objects verified:

- `AppBankConnections`, `AppBankAccounts`, `AppBankTransactions`,
  `AppBankSyncRuns`, and `AppBankConsentRecords`.
- All columns, SQL types, lengths, precision, and nullability.
- Five primary keys, ten secondary indexes, nine enabled/trusted foreign keys,
  and three enabled/trusted check constraints.
- Client foreign keys use NO ACTION; Banking parent-child foreign keys use CASCADE.

Action taken:

- The already-applied handwritten foundation lacked a designer target model
  and snapshot; the runtime model also omitted its existing foreign keys.
- Aligned the model with the applied schema. A regression test compares EF's
  relational model operations against the original foundation operations.
- Used `dotnet ef migrations add BankingFoundation` with the old source
  temporarily excluded to generate legitimate metadata. Kept the generated
  snapshot unchanged and attached the generated designer to the original
  applied migration ID. Preserved the original Up/Down operations; no duplicate
  foundation or empty follow-up migration remains.
- Mapped the client reference with `ExcludeFromMigrations()`: Portal remains
  the owner of `AppClients`.
- Explicitly adopted the verified existing foundation history into the new
  development-only Banking history table; no Banking tables were recreated.

## Context isolation and startup

Both contexts use migration assembly `SecureClientPortal.Infrastructure`.
Context attributes keep EF discovery disjoint despite the shared assembly.

Portal migrations/snapshot remain under
`EntityFrameworkCore/MigrationsSqlServer/MigrationsSqlServer`.
Banking migrations/designer/snapshot are under
`EntityFrameworkCore/MigrationsSqlServer/Banking`.

Histories in the same physical development database:

- Portal: `dbo.__EFMigrationsHistory`.
- Banking: `dbo.__EFMigrationsHistory_Banking`.

The original shared history retains its legacy Banking record deliberately;
Portal ignores it because it is not in Portal's discovered chain. Banking now
uses only its dedicated history. No history records were deleted.

Startup checks Portal first, then Banking. A read-only guard refuses to replay
Banking foundation when Banking tables exist without its dedicated history.
There is no automatic history adoption at runtime. Any other existing database
must have an independently reviewed history transition before deploying this
configuration; the supplied adoption runner is restricted to local development.

## Safety and execution evidence

Before the database change, the pending foundation caused by switching to an
empty dedicated history was identified. The reviewed SQL would only create
`dbo.__EFMigrationsHistory_Banking` and its primary key, then copy the original
verified migration ID/version. It contained no Compliance/Banking data-table
DROP, ALTER, or replayed CREATE operations.

A COPY_ONLY backup with CHECKSUM was taken before adoption and passed
`RESTORE VERIFYONLY WITH CHECKSUM`:
`/var/opt/mssql/datasecure_client_portal_dev_migration_verification_20260916_190100.bak`.
The backup script subsequently received a path-separator correction for future
runs; this actual backup was verified successfully at the path above.

The guarded adoption SQL validates the five Banking tables, nine foreign keys,
ten indexes, and three checks, and copies history transactionally. Both EF
`database update` commands then reported the database already up to date.
Before/after inventories show identical Compliance/Banking schema and row counts.

Repeatable tools:

- `scripts/Invoke-DevelopmentDatabaseSql.ps1`: refuses non-local/non-development targets.
- `scripts/Inspect-DatabaseSchema.sql`: read-only catalog/history inventory.
- `scripts/Backup-DevelopmentDatabase.sql`: COPY_ONLY backup and verification.
- `scripts/Adopt-BankingMigrationHistory.sql`: explicit, guarded development transition.

EF commands were run separately for each context from the repository root:

```powershell
dotnet ef migrations list --project src/SecureClientPortal.Infrastructure --startup-project src/SecureClientPortal.Api --context PortalDbContext --configuration Release --no-build -- --environment Development
dotnet ef migrations list --project src/SecureClientPortal.Infrastructure --startup-project src/SecureClientPortal.Api --context BankingDbContext --configuration Release --no-build -- --environment Development
dotnet ef migrations has-pending-model-changes --project src/SecureClientPortal.Infrastructure --startup-project src/SecureClientPortal.Api --context PortalDbContext --configuration Release --no-build -- --environment Development
dotnet ef migrations has-pending-model-changes --project src/SecureClientPortal.Infrastructure --startup-project src/SecureClientPortal.Api --context BankingDbContext --configuration Release --no-build -- --environment Development
```

Full Release solution build: succeeded, zero errors; existing xUnit analyzer
warnings remain. Full backend tests: 223 passed, zero failed, zero skipped.

Release API started in Development at `http://localhost:5128`, PID `49724`.
Both startup migration-success logs report pending count 0. Standard error is
empty, and `GET /api/auth/csrf` returned HTTP 200. No EF startup errors were found.

Local execution evidence is in ignored `artifacts/database-verification/`:
`schema-before.txt`, `schema-after.txt`, `backup-verification.txt`,
`api-stdout.log`, `api-stderr.log`, and `api-process.json`.

Final verification state: both contexts have clean migration and model states
on the verified development database. Verification completed on main before
the subsequent commit and push requested by the user.
