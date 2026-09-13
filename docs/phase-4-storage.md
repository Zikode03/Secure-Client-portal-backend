# Phase 4 — Documents and encryption

## Implemented

New uploads flow directly from multipart request streams into encrypted, bounded
64 KiB records in a private `.quarantine` directory. Metadata must precede the
single File part; both Documents and Inbox clients now send this ordering.
Declared lengths are not trusted: storage counts/enforces actual bytes. Inbox
requests retain their smaller 25 MiB request-body limit.

Each record authenticates file identity, sequence and the terminal record using
ASP.NET Data Protection. The ClamAV INSTREAM integration scans the decrypted stream;
only an exact clean verdict promotes the encrypted file into available storage.
Infected, malformed, timed-out or unavailable scanner results fail closed and
remove the rejected pending file. Crashed processes may leave encrypted pending
files; they are inaccessible and count against quota until reviewed/removed
during maintenance. No file is registered as an available document before scanning.

An authenticated scan receipt binds the scanner verdict to the storage key and encrypted file identity. Files without a valid ClamAV receipt (including development baseline files) are scanned before download; encryption alone is not treated as proof of antivirus clearance.

File format checks supplement antivirus. Production cannot select the development
baseline scanner. Encrypted SCP1 and plaintext legacy files are rescanned and
migrated on read, with a bounded legacy allocation (up to 100 MB); new uploads and
SCP2 downloads are streamed. Plan an offline migration before serving a large
legacy archive. Do not equate legacy baseline scanning with antivirus clearance.

Storage keys cannot be injected via metadata create/update endpoints. Quarantine,
path traversal and symbolic links are blocked. Upload publication and delete are
serialized by a shared-filesystem lock to enforce aggregate/per-client quotas.
Downloads are limited per authenticated user per API instance; configure a matching
aggregate limit at the proxy when running multiple instances. Network byte-rate
and concurrent-connection limits should also be applied at the reverse proxy.

## Required production infrastructure

Use a durable SMB3/shared filesystem with verified cross-host exclusive-lock
semantics, mounted outside application release directories. Provision separate
document and key-ring directories; grant access only to API/backup operators.
Do not expose them as static web folders. Both directories must already exist.
Place a `.portal-volume` text file containing the same deployment-specific volume
identifier in each; startup checks it to detect a missing/wrong mount.

| Environment variable | Required/recommended setting |
| --- | --- |
| Storage__Provider | shared |
| Storage__RootPath | Absolute persistent document directory |
| Storage__KeyRingPath | Separate absolute persistent key-ring directory |
| Storage__VolumeId | Identifier in both .portal-volume files |
| Storage__CertificatePath | Mounted private-key PFX certificate, outside source |
| Storage__CertificatePassword | Certificate password from the host secret manager |
| Storage__PreviousCertificatePaths__0 | Retired PFX needed for older keys, when rotating |
| Storage__PreviousCertificatePasswords__0 | Matching retired certificate password, when different |
| Storage__Scanner | clamav |
| Storage__ClamAvHost / Storage__ClamAvPort | Private clamd endpoint / 3310 |
| Storage__ScanTimeoutSeconds | 120 (maximum 300) |
| Storage__MaxFileBytes | 100000000 maximum |
| Storage__ClientQuotaBytes | 5000000000 default, includes retained versions/quarantine |
| Storage__TotalQuotaBytes | 100000000000 default |
| Storage__DownloadsPerMinute | 30 default, per user per instance |

Use an RSA certificate with private key and valid dates. All instances use the
same application name, key ring and decrypting certificates. Rotation must retain
old certificates and keys until their data has been migrated and backups retired.
Never delete old keys to resolve a decryption error. Configuring certificate
protection does not re-encrypt pre-existing plaintext key XML: production rejects
that condition; rewrap keys during controlled maintenance, retaining a verified
backup, before startup. This implementation does not silently rewrite old keys.

Provision ClamAV/clamd and freshclam with persistent signature data. Set
StreamMaxLength/MaxScanSize above the upload limit; enable archive scanning and
AlertExceedsMax so limits/encrypted unsupported formats are not silently treated
as clean. Restrict clamd to the private API network; its TCP protocol has no TLS
or authentication. Use a secured tunnel/sidecar if the network is not trusted.
Maintain signature update monitoring; test EICAR and scanner-unavailable cases
on staging. This repository integration does not provision or start ClamAV.

Development explicitly retains the baseline scanner for compatibility; set
Storage__Scanner=clamav and configure clamd to test real scanning locally.

## Coordinated backup and restore

`tools/Backup-PortalData.ps1` creates a new snapshot containing encrypted documents,
key XML, encrypted PFX archives (including old certificates), a supplied SQL backup,
and a SHA-256 manifest. It never overwrites a previous backup. Pause all API
instances/writers, create the database backup, then run the script with
-MaintenanceConfirmed. Keep certificate passwords separately. The script is a
maintenance tool, not an automated backup schedule or an encrypted remote vault.

Example placeholders (replace paths; do not put passwords in the command):

```powershell
./tools/Backup-PortalData.ps1 -DocumentRoot D:/PortalData/documents -KeyRingRoot D:/PortalData/keys -DatabaseBackupPath D:/SqlBackups/portal.bak -EncryptedCertificatePaths D:/Secrets/portal-current.pfx,D:/Secrets/portal-old.pfx -Destination E:/PortalBackups/snapshot-2026-09-10 -MaintenanceConfirmed
```

Schedule an encrypted off-host backup job with your infrastructure provider.
Protect the snapshot because it includes the database and private-key material.
Copying documents without their keys/certificates is not a recoverable backup.

Restore drill: restore into isolated directories/database; verify every manifest
hash, configure the original application name and required certificates, then
download representative current/old document versions and verify checksums.
Verify MFA secrets remain usable, test new uploads with ClamAV, and repeat after
certificate rotation. Never test restores by overwriting production.

Gate remains open until a real persistent mount, certificate, ClamAV with fresh
signatures and off-host backup schedule are provisioned and a redeployment/restore
drill succeeds. Automated tests verify encrypted files decrypt with a new provider
and copied key/document directories; they cannot prove your hosting mounts.

References: [Microsoft streaming uploads](https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads?view=aspnetcore-8.0),
[Data Protection configuration](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-8.0),
[ClamAV protocol](https://docs.clamav.net/manual/Usage/ClamdProtocol.html).
