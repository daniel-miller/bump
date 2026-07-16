# Restore the local work database from a backup file (pg_dump custom format).
# Terminates existing connections, then drops, creates, and restores the
# database. By default restores the newest backup in $BackupFolder; pass
# -BackupFile to restore a specific file.
[CmdletBinding()]
param(
    [string] $BackupFile,
    [string] $BackupFolder = 'E:\base\srv\host\djm\live\backups',
    [string] $Database = 'bump',
    [string] $Server = 'localhost',
    [int]    $Port = 5432,
    [string] $Username = 'postgres',
    [string] $Password = $env:PGPASSWORD
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw 'Password required via -Password parameter or PGPASSWORD environment variable.'
}

# psql, pg_dump, and pg_restore all read the password from PGPASSWORD. The
# variable stays set in the caller's session afterward: it is the developer's
# own local password, and every db script in every repo reads the same source.
$env:PGPASSWORD = $Password

# Note: $BackupFile (the [string] parameter) and $backupFile are the same
# case-insensitive variable, so the FileInfo object must go in a differently
# named variable or it silently degrades to a string with no .FullName.
if ($BackupFile) {
    if (-not (Test-Path $BackupFile)) {
        throw "Backup file not found: $BackupFile"
    }
    $backup = Get-Item $BackupFile
}
else {
    $backup = Get-ChildItem -Path $BackupFolder -Filter "bump_*.backup" |
    Where-Object { $_.Name -match '^bump_\d{8}_\d{6}\.backup$' } |
    Sort-Object Name -Descending |
    Select-Object -First 1

    if (-not $backup) {
        throw "No backup file matching 'bump_yyyyMMdd_HHmmss.backup' found in $BackupFolder"
    }
}

Write-Host "Restoring '$Database' from $($backup.FullName)..." -ForegroundColor Cyan

# Validate the archive before dropping anything; a bad file caught after the
# drop would leave an empty database.
pg_restore -l $backup.FullName > $null
if ($LASTEXITCODE -ne 0) {
    throw "Not a valid pg_restore archive: $($backup.FullName)"
}

$psqlBase = @('-h', $Server, '-p', $Port, '-U', $Username,
    '-v', 'ON_ERROR_STOP=1', '--quiet', '--no-psqlrc')

# Every psql call must be checked: an unnoticed DROP failure lets the restore
# run against the surviving database and merge into live data.
function Invoke-Psql {
    param(
        [Parameter(Mandatory)] [string] $Sql,
        [string] $OnDatabase = 'postgres'
    )
    psql @psqlBase '-d' $OnDatabase '-c' $Sql
    if ($LASTEXITCODE -ne 0) { throw "psql failed against '$OnDatabase': $Sql" }
}

# Same contract as Invoke-Psql, but returns the single value the query selects.
function Get-PsqlValue {
    param(
        [Parameter(Mandatory)] [string] $Sql,
        [string] $OnDatabase = 'postgres'
    )
    $value = psql @psqlBase '-d' $OnDatabase '--tuples-only' '--no-align' '-c' $Sql
    if ($LASTEXITCODE -ne 0) { throw "psql failed against '$OnDatabase': $Sql" }
    return ($value | Select-Object -First 1)
}

Invoke-Psql "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$Database' AND pid <> pg_backend_pid();"
Invoke-Psql "DROP DATABASE IF EXISTS $Database;"
Invoke-Psql "CREATE DATABASE $Database;"

# --no-owner / --no-privileges: the backup comes from the live server and
# references roles that do not exist on a developer machine.
pg_restore -h $Server -p $Port -U $Username -d $Database -F c --no-owner --no-privileges -j 4 $backup.FullName
if ($LASTEXITCODE -ne 0) {
    throw "pg_restore failed with exit code $LASTEXITCODE"
}

# Migrations newer than the backup are applied by Bump.Api on boot; the
# Migrator runs at startup and there is no CLI to invoke here.

# CREATE DATABASE above names no tablespace, so the files always land in the
# cluster's default one: <data_directory>/base/<oid>. data_directory is
# superuser-only, so reporting must never fail a restore that already worked.
$location = $null
try {
    $location = Get-PsqlValue "SELECT current_setting('data_directory') || '/base/' || oid FROM pg_database WHERE datname = '$Database';"
    $size = Get-PsqlValue "SELECT pg_size_pretty(pg_database_size('$Database'));"
}
catch {
    Write-Warning "Could not read the database location: $($_.Exception.Message)"
    # Clear psql's exit code: the restore itself succeeded, and leaving 1 here
    # makes the script exit 1 and the caller read a good restore as a failure.
    $global:LASTEXITCODE = 0
}

Write-Host ""
Write-Host "Database '$Database' restored. Start Bump.Api to apply any newer migrations." -ForegroundColor Green
if ($location) {
    Write-Host "  Files: $location"
    Write-Host "  Size:  $size"
}
