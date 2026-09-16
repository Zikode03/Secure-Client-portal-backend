param([Parameter(Mandatory)][string]$SqlFile)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = Get-Content (Join-Path $root 'src/SecureClientPortal.Api/appsettings.Development.json') -Raw | ConvertFrom-Json
$connection = if ($env:DB_CONNECTION_STRING) { $env:DB_CONNECTION_STRING }
elseif ($env:ConnectionStrings__DefaultConnection) { $env:ConnectionStrings__DefaultConnection }
else { $config.ConnectionStrings.DefaultConnection }
$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.set_ConnectionString($connection)
$server = [string]$builder.get_Item('Server')
$database = [string]$builder.get_Item('Database')
if ($server -notin @('localhost,1433', '127.0.0.1,1433') -or $database -ne 'secure_client_portal_dev') {
    throw 'Refusing SQL execution: the connection is not the verified local DEVELOPMENT database.'
}
$previousPassword = $env:SQLCMDPASSWORD
try {
    $env:SQLCMDPASSWORD = [string]$builder.get_Item('Password')
    Write-Output "Development SQL target: $server / $database"
    & sqlcmd -S $server -d $database -U ([string]$builder.get_Item('User Id')) -N -C -b -l 15 -W -s '|' -i (Resolve-Path -LiteralPath $SqlFile).Path
    if ($LASTEXITCODE -ne 0) { throw "SQL verification failed (exit $LASTEXITCODE)." }
}
finally { $env:SQLCMDPASSWORD = $previousPassword }
