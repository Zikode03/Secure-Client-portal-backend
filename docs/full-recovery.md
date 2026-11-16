# Complete portal backup and recovery

Back up and restore a coordinated set: SQL database, encrypted document volume (including versions, staged imports and scan receipts), data-protection key ring, and every current/previous private key-protection PFX certificate. A database-only backup cannot recover document contents or MFA secrets.

## Preconditions

- Stop **all API instances and background workers** and drain requests first. `--offline` / `-OfflineAcknowledged` is a required operator acknowledgement, not an automatic proof that other machines are stopped. The tool does not support live, uncoordinated copies.
- Use private SQL staging accessible to both SQL Server and the tool. `--sql-staging` is the directory as SQL Server sees it; `--local-staging` is the same directory as the tool sees it (local path, SMB or container bind mount). Restrict both to the backup operator and SQL service identity. Use encrypted disks for staging, including the operator's temporary directory: a temporary plaintext ZIP is needed while creating/verifying a bundle.
- Have free private staging space for multiple copies of the dataset during verification. Do not put output bundles inside the document or key directories. Symlinks/reparse points in the copied trees are rejected.
- Exportable private PFX certificates and all their passwords must be available. Keep the separate recovery passphrase and certificate passwords in a protected secret vault; losing them makes recovery impossible. Do not store them in Git, arguments or alongside the bundle.
- The backup identity needs SQL backup/checksum verification and read access to the document-reference tables. Restore needs database-creation, restore and CHECKDB rights. Default server data/log directories must be writable by SQL Server.
- .NET 8 SDK is required for the wrappers, or pre-publish PortalRecovery for the recovery machine.

## Secret environment

Load these values through a protected secret-management mechanism:

- `SQLCMDPASSWORD`: SQL password when `--user` / `-User` is supplied. Omit User to use integrated authentication. No password is passed as an argument.
- `PORTAL_RECOVERY_PASSPHRASE`: independent strong backup passphrase, at least 20 characters.
- `PORTAL_RECOVERY_CERT_PASSWORDS_JSON`: JSON array of certificate passwords, in the same order as repeated `--certificate` arguments. The first certificate should be current; include all previous certificates still needed by retained keys.

## Backup

Example paths are placeholders, not credentials. Run from the backend root after stopping writers:

```powershell
./database/scripts/backup-sqlserver.ps1 -Server 'sql.example.internal' -Database 'SecureClientPortal' -User 'backup_operator' -DocumentsPath 'D:/PortalData/documents' -KeyRingPath 'D:/PortalKeys' -CertificateFiles @('D:/Vault/current.pfx','D:/Vault/previous.pfx') -SqlStagingPath 'D:/SqlRecovery' -LocalStagingPath 'D:/SqlRecovery' -OutputBundle 'E:/Backups/portal-2026-11-16.scpbak' -OfflineAcknowledged
```

Production uses validated TLS by default. `-TrustServerCertificate` / `--trust-server-certificate` is an explicit exception for an isolated development/rehearsal SQL server, not a production default.

The tool checks current database document references, uses SQL `COPY_ONLY`, `CHECKSUM`, `COMPRESSION`, runs `RESTORE VERIFYONLY`, packages a versioned size/SHA-256 manifest, and verifies every encryption-key descriptor and protected document. The bundle is encrypted with AES-256-GCM records using the existing authenticated streaming framing, with an independent PBKDF2-SHA256-derived key (600,000 iterations and random salt). It is reopened and fully verified before the final filename is published. Partial output and owned temporary files are cleaned up on failure; native/tool errors produce a nonzero exit and no success message.

The raw SQL `.bak` is private staging only and is removed after the operation. Cleanup failures are errors and must be investigated. Copy the encrypted bundle to access-controlled offsite/immutable storage and test retention policies; this tool does not provision a storage provider or schedule backups.

Verify a stored copy without restoring a database:

```powershell
dotnet run --project tools/PortalRecovery -c Release -- verify --bundle 'E:/Backups/portal-2026-11-16.scpbak'
```

## Restore into a NEW isolated target

```powershell
./database/scripts/restore-sqlserver.ps1 -Server 'recovery-sql.example.internal' -Database 'SecureClientPortal_Recovery_20261116' -User 'restore_operator' -Bundle 'E:/Backups/portal-2026-11-16.scpbak' -Destination 'D:/PortalRecovery/20261116' -SqlStagingPath 'D:/SqlRecovery' -LocalStagingPath 'D:/SqlRecovery' -OfflineAcknowledged
```

Existing databases and existing destinations are refused. Restore never uses `WITH REPLACE` or disconnects an existing database. It authenticates the full archive before extraction, rejects traversal/duplicate paths and excessive sizes, verifies manifest checksums and document/key decryption, restores SQL using fresh physical data/log files, runs `DBCC CHECKDB`, and checks database references against restored files. Special FILESTREAM files require a separate recovery plan and are refused.

Restored login sessions and one-time access tokens are invalidated, so old backup-era authentication tokens cannot be replayed. A failed restore may leave the **new** isolated database/destination for investigation; it is not switched into service and is never reported as successful.

Before enabling access:

1. Point a stopped API at the new database and `Destination/Documents`, `Destination/KeyRing`, and `Destination/Certificates/0.pfx` plus previous certificates. Configure the matching certificate passwords, storage provider, persistent-volume markers and scanner. The Data Protection application name must stay `SecureClientPortal`.
2. Obtain other deployment secrets (JWT, SMTP and external credentials) from your secret vault. They are deliberately not included in the bundle. Prefer a fresh JWT signing key after disaster recovery.
3. Review account status, business memberships and role changes made after the backup. A point-in-time backup also restores old authorization/recovery-code state; reconcile that history before reopening access. Reissue outstanding invitations/resets as needed.
4. Start an isolated API with email/automation delivery disabled. Confirm sign-in/MFA, the filing register and representative historical/current document downloads. Only then plan a controlled cutover. Preserve the existing system until the recovered system is accepted.

## Automated rehearsal

The backend GitHub workflow provisions a separate SQL Server container and runs `PortalRecovery smoke`. It creates two uniquely named test databases and generated certificate/document fixtures, performs a real SQL backup, creates the encrypted bundle, deletes the original test database/files, then restores solely from the bundle. It checks SQL integrity, file references, exact decrypted contents and invalidation of sessions/tokens. It deletes only its own generated test resources. No client database or actual financial document is used.

Unit tests cover encrypted round trips, corruption, truncation, wrong passwords, traversal and refusal to overwrite a destination. A successful CI rehearsal proves the mechanism with isolated fixtures; production storage permissions, offsite retention and a rehearsal against the actual deployed dataset must still be verified operationally.
