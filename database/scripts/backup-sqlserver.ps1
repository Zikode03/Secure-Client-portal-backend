[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][string]$Database,
    [Parameter(Mandatory)][string]$DocumentsPath,
    [Parameter(Mandatory)][string]$KeyRingPath,
    [Parameter(Mandatory)][string[]]$CertificateFiles,
    [Parameter(Mandatory)][string]$SqlStagingPath,
    [Parameter(Mandatory)][string]$LocalStagingPath,
    [Parameter(Mandatory)][string]$OutputBundle,
    [Parameter(Mandatory)][switch]$OfflineAcknowledged,
    [string]$User,
    [switch]$TrustServerCertificate
)
$ErrorActionPreference = "Stop"
if (-not $OfflineAcknowledged) { throw "Stop every API instance and background worker before continuing." }
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../tools/PortalRecovery/PortalRecovery.csproj"))
$recoveryArgs = @("backup", "--offline", "--server", $Server, "--database", $Database,
    "--documents", $DocumentsPath, "--keys", $KeyRingPath,
    "--sql-staging", $SqlStagingPath, "--local-staging", $LocalStagingPath, "--output", $OutputBundle)
foreach ($certificate in $CertificateFiles) { $recoveryArgs += @("--certificate", $certificate) }
if ($User) { $recoveryArgs += @("--user", $User) }
if ($TrustServerCertificate) { $recoveryArgs += "--trust-server-certificate" }
# Secrets are read from the environment by the tool, never placed on a command line.
& dotnet run --project $project --configuration Release -- @recoveryArgs
if ($LASTEXITCODE -ne 0) { throw "Full portal backup failed (exit $LASTEXITCODE). No successful backup is being reported." }
