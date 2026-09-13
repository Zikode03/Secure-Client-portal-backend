[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DocumentRoot,
    [Parameter(Mandatory)][string]$KeyRingRoot,
    [Parameter(Mandatory)][string]$DatabaseBackupPath,
    [Parameter(Mandatory)][string[]]$EncryptedCertificatePaths,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][switch]$MaintenanceConfirmed
)
$ErrorActionPreference = 'Stop'
if (-not $MaintenanceConfirmed) { throw 'Pause all API instances and database writes before taking the database backup and running this snapshot.' }
$docPath = (Resolve-Path -LiteralPath $DocumentRoot).Path
$keyPath = (Resolve-Path -LiteralPath $KeyRingRoot).Path
$dbBackup = (Resolve-Path -LiteralPath $DatabaseBackupPath).Path
$backupPath = [IO.Path]::GetFullPath($Destination)
foreach ($sourcePath in @($docPath, $keyPath)) {
    if ($backupPath.StartsWith($sourcePath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $backupPath -eq $sourcePath) {
        throw 'Backup destination must be outside the source directories.'
    }
}
if (Test-Path -LiteralPath $backupPath) { throw 'Use a new, empty backup destination. Existing backups are never overwritten.' }
foreach ($sourcePath in @($docPath,$keyPath)) {
    if ((Get-ChildItem -LiteralPath $sourcePath -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) { throw 'Source contains links; review paths before backup.' }
}
$certificates = @($EncryptedCertificatePaths | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
foreach ($certificate in $certificates) {
    if ([IO.Path]::GetExtension($certificate) -notin @('.pfx','.p12')) { throw 'Supply password-protected certificate archives, including retired certificates still needed for decryption.' }
}
$gate = [IO.File]::Open((Join-Path $docPath '.quota.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    New-Item -ItemType Directory -Path $backupPath | Out-Null
    $documentBackup = Join-Path $backupPath 'documents'
    New-Item -ItemType Directory -Path $documentBackup | Out-Null
    Get-ChildItem -LiteralPath $docPath -Force | Where-Object Name -ne '.quota.lock' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $documentBackup -Recurse -Force }
    Copy-Item -LiteralPath $keyPath -Destination (Join-Path $backupPath 'keyring') -Recurse
    Copy-Item -LiteralPath $dbBackup -Destination (Join-Path $backupPath 'database.bak')
    $certDir = Join-Path $backupPath 'certificates'
    New-Item -ItemType Directory -Path $certDir | Out-Null
    $index = 0
    foreach ($certificate in $certificates) { Copy-Item -LiteralPath $certificate -Destination (Join-Path $certDir ("certificate-{0}.pfx" -f $index)); $index++ }
    $manifest = Get-ChildItem -LiteralPath $backupPath -File -Recurse -Force | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($backupPath,$_.FullName); Length = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $backupPath 'manifest.json') -Encoding utf8
    Write-Output 'Snapshot complete. Store it encrypted off-host; keep certificate passwords separately. Verify hashes and perform a restore drill before relying on this backup.'
} finally { $gate.Dispose() }
