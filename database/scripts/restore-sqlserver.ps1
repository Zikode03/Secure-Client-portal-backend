[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][string]$Database,
    [Parameter(Mandatory)][string]$Bundle,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$SqlStagingPath,
    [Parameter(Mandatory)][string]$LocalStagingPath,
    [Parameter(Mandatory)][switch]$OfflineAcknowledged,
    [string]$User,
    [switch]$TrustServerCertificate
)
$ErrorActionPreference = "Stop"
if (-not $OfflineAcknowledged) { throw "Stop every API instance and background worker before continuing." }
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../tools/PortalRecovery/PortalRecovery.csproj"))
$recoveryArgs = @("restore", "--offline", "--server", $Server, "--database", $Database,
    "--bundle", $Bundle, "--destination", $Destination,
    "--sql-staging", $SqlStagingPath, "--local-staging", $LocalStagingPath)
if ($User) { $recoveryArgs += @("--user", $User) }
if ($TrustServerCertificate) { $recoveryArgs += "--trust-server-certificate" }
# Restore refuses existing databases and destinations; no WITH REPLACE or forced disconnect.
& dotnet run --project $project --configuration Release -- @recoveryArgs
if ($LASTEXITCODE -ne 0) { throw "Portal restore failed (exit $LASTEXITCODE). Keep the API offline and inspect the new recovery target." }
