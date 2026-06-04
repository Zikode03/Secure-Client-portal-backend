param(
    [Parameter(Mandatory = $true)]
    [string]$Server,
    [Parameter(Mandatory = $true)]
    [string]$Database,
    [Parameter(Mandatory = $true)]
    [string]$User,
    [Parameter(Mandatory = $true)]
    [string]$Password,
    [string]$BackupDirectory = ".\database\backups"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BackupDirectory)) {
    New-Item -ItemType Directory -Path $BackupDirectory | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupPath = Join-Path $BackupDirectory "$Database`_$timestamp.bak"

$query = "BACKUP DATABASE [$Database] TO DISK = N'$backupPath' WITH INIT, COMPRESSION, STATS = 10;"

sqlcmd -S $Server -U $User -P $Password -Q $query

Write-Host "Backup complete: $backupPath"
