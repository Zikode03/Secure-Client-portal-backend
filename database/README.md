# Database

- migrations/: schema migrations
- seeds/: sample seed data (no production data)
- schema/: schema definition files
- scripts/: helper SQL scripts

## Migration Commands (EF Core)

From repo root:

```powershell
dotnet ef database update --project backend/SecureClientPortal.Backend
```

When model changes are made:

```powershell
dotnet ef migrations add <MigrationName> --project backend/SecureClientPortal.Backend --output-dir Migrations
dotnet ef database update --project backend/SecureClientPortal.Backend
```

## Backup / Restore (SQL Server)

- Backup:

```powershell
powershell -ExecutionPolicy Bypass -File database/scripts/backup-sqlserver.ps1 -Server "localhost,1433" -Database "SecureClientPortal_Dev" -User "sa" -Password "<password>"
```

- Restore:

```powershell
powershell -ExecutionPolicy Bypass -File database/scripts/restore-sqlserver.ps1 -Server "localhost,1433" -Database "SecureClientPortal_Dev" -User "sa" -Password "<password>" -BackupFile "database/backups/SecureClientPortal_Dev_YYYYMMDD_HHMMSS.bak"
```

## Filing Register Update

Automatic filing support is now included in backend schema and API.

- `AppDocuments` now tracks filed-state metadata:
  - `IsFiled` (`bit`)
  - `FiledAtUtc` (`datetime2`, nullable)
  - `FiledByUserId` (`nvarchar(100)`, nullable)
- New table `AppFilingRules` controls which document categories are eligible for auto-filing when accepted.

Migration shipped:

- `20260519110000_AddFilingRulesAndDocumentFiledFlags`
