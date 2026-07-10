# Restores the local work database from a backup file (pg_dump custom format).
# Terminates existing connections, drops and recreates the database, then
# restores. Migrations newer than the backup are applied automatically the
# next time Bump.Api starts (the Migrator runs on boot).
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BackupFile,
    [string] $Database = 'bump',
    [string] $Server = 'localhost',
    [int]    $Port = 5432,
    [string] $Username = 'postgres',
    # Placeholder default: pass -Password with your local postgres password.
    [string] $Password = 'YOUR_LOCAL_PASSWORD'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $BackupFile)) {
    throw "Backup file not found: $BackupFile"
}

$env:PGPASSWORD = $Password

$baseArgs = @('-h', $Server, '-p', $Port, '-U', $Username, '-d', 'postgres',
              '-v', 'ON_ERROR_STOP=1', '--quiet', '--no-psqlrc')

function Invoke-PsqlCommand {
    param([Parameter(Mandatory)] [string]$Sql)
    psql @baseArgs '-c' $Sql
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $Sql" }
}

Write-Host "Restoring '$Database' from $(Split-Path -Leaf $BackupFile)..." -ForegroundColor Cyan

Invoke-PsqlCommand "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$Database' AND pid <> pg_backend_pid();"
Invoke-PsqlCommand "DROP DATABASE IF EXISTS $Database;"
Invoke-PsqlCommand "CREATE DATABASE $Database;"

pg_restore -h $Server -p $Port -U $Username -d $Database -F c --no-owner --no-privileges -j 4 $BackupFile
if ($LASTEXITCODE -ne 0) {
    throw "pg_restore failed with exit code $LASTEXITCODE"
}

Write-Host "Database '$Database' restored. Start Bump.Api to apply any newer migrations." -ForegroundColor Green
