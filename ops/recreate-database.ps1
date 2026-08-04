# Recreate the bump database from the fresh owner-naming baseline.
#
# One-shot cutover script for the 2026-08 tenant-to-owner rebuild. It captures
# the in-use app version numbers from the existing database (mapping old app
# names to their new codename handles), drops the database, recreates it,
# applies the migration baseline, seeds the canon rosters, restores the
# captured versions, and applies the admin account seed when one is provided.
#
# Everything else in the old database is deliberately discarded: probe events,
# problems, outages, subscribers, and sessions all start empty.
#
# Deployed to c:\srv\opt\<owner>\<environment>\scripts\ alongside the app.
# -DbDir must point at the db folder shipped with the deployed package (the
# folder holding migrations\ and seed-canon.sql); it defaults to the repo
# layout so the script also runs from a developer clone.

param(
    [string]$DbHost = 'localhost',
    [int]$Port = 5432,
    [string]$User = 'postgres',
    [string]$Password = $env:PGPASSWORD,
    [string]$Database = 'bump',
    [string]$DbDir = '',
    # Path to a SQL file that inserts the admin account (kept in the secrets
    # collection, never in the repo). Skipped with a warning when empty.
    [string]$AdminSeed = '',
    # The drop is destructive and this script may be pointed at production.
    # Without -Force it reports what it would do and exits.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw 'Password required via -Password parameter or PGPASSWORD environment variable.'
}

if ([string]::IsNullOrWhiteSpace($DbDir)) {
    $DbDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'db'
}
$migrationsDir = Join-Path $DbDir 'migrations'
$seedCanon = Join-Path $DbDir 'seed-canon.sql'
$seedTheme = Join-Path $DbDir 'seed-openscorm-theme.sql'
if (-not (Test-Path $migrationsDir)) { throw "Migrations folder not found: $migrationsDir" }
if (-not (Test-Path $seedCanon)) { throw "Canon seed not found: $seedCanon" }

# Old app slug -> new codename (the app_handle in the fresh baseline).
# Identity entries make the script re-runnable against an already-migrated
# database. Names absent from this table are dropped on purpose (cmds,
# danielmiller, engine, spark-web); their versions are printed so nothing
# disappears silently.
$handleMap = @{
    'bump'            = 'bump'
    'spark'           = 'spark'
    'bridgemarket'    = 'bridge'
    'bridge'          = 'bridge'
    'openscorm'       = 'slate'
    'slate'           = 'slate'
    'pgmusicfestival' = 'festival'
    'festival'        = 'festival'
}

$env:PGPASSWORD = $Password

$baseArgs = @(
    '-h', $DbHost,
    '-p', $Port,
    '-U', $User,
    '-v', 'ON_ERROR_STOP=1',
    '--quiet',
    '--no-psqlrc'
)

function Invoke-PsqlCommand {
    param(
        [Parameter(Mandatory)] [string]$DbName,
        [Parameter(Mandatory)] [string]$Sql
    )
    psql @baseArgs '-d' $DbName '-c' $Sql
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $Sql" }
}

function Invoke-PsqlFile {
    param(
        [Parameter(Mandatory)] [string]$DbName,
        [Parameter(Mandatory)] [string]$File
    )
    psql @baseArgs '-d' $DbName '--single-transaction' '-f' $File
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $(Split-Path -Leaf $File)" }
}

function Invoke-Migration {
    # Run migration file + history insert in a single transaction,
    # mirroring Bump.Api/Migrations/Migrator.cs.
    param(
        [Parameter(Mandatory)] [string]$DbName,
        [Parameter(Mandatory)] [string]$File
    )
    $name = Split-Path -Leaf $File
    $escaped = $name.Replace("'", "''")
    $body = Get-Content $File -Raw
    $combined = @"
$body
INSERT INTO _migration_history(name, applied_at) VALUES ('$escaped', now());
"@
    $combined | psql @baseArgs '-d' $DbName '--single-transaction' '-f' '-'
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $name" }
}

# ---- 1. Capture in-use versions from the existing database ----

$captured = @()
$dbExists = (psql @baseArgs '-d' 'postgres' '-t' '-A' '-c' "SELECT 1 FROM pg_database WHERE datname = '$Database'") -eq '1'

if ($dbExists) {
    # The pre-cutover schema names the column app_slug; the fresh baseline
    # names it app_handle. Detect which one the existing database carries so
    # the script works for the cutover and for any re-run after it.
    $idColumn = (psql @baseArgs '-d' $Database '-t' '-A' '-c' `
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'app' AND column_name IN ('app_handle','app_slug') ORDER BY column_name LIMIT 1;").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($idColumn)) {
        throw 'Could not determine the app identifier column in the existing database.'
    }

    Write-Host "Capturing app versions from '$Database' ($idColumn)..." -ForegroundColor Cyan
    $rows = psql @baseArgs '-d' $Database '-t' '-A' '-F' '|' '-c' `
        "SELECT $idColumn, version_major, version_minor, version_patch FROM app ORDER BY $idColumn;"
    if ($LASTEXITCODE -ne 0) { throw 'Failed to read app versions from the existing database.' }
    foreach ($row in @($rows) | Where-Object { $_ }) {
        $parts = $row.Split('|')
        $old = $parts[0]
        $version = "$($parts[1]).$($parts[2]).$($parts[3])"
        if ($handleMap.ContainsKey($old)) {
            $captured += [pscustomobject]@{ NewHandle = $handleMap[$old]; Major = $parts[1]; Minor = $parts[2]; Patch = $parts[3] }
            Write-Host "  $old $version -> $($handleMap[$old])" -ForegroundColor DarkGray
        }
        else {
            Write-Host "  $old $version -> dropped (not in handle map)" -ForegroundColor Yellow
        }
    }
}
else {
    Write-Host "Database '$Database' does not exist; nothing to capture." -ForegroundColor Yellow
}

if (-not $Force) {
    Write-Host ''
    Write-Host "Dry run. Would drop and recreate '$Database' on $DbHost, apply migrations from" -ForegroundColor Yellow
    Write-Host "$migrationsDir, seed canon rosters, and restore $($captured.Count) app versions." -ForegroundColor Yellow
    Write-Host 'Re-run with -Force to proceed.' -ForegroundColor Yellow
    exit 1
}

# ---- 2. Drop and recreate ----

Write-Host "Dropping database '$Database'..." -ForegroundColor Cyan
Invoke-PsqlCommand -DbName postgres -Sql "DROP DATABASE IF EXISTS $Database WITH (FORCE);"

Write-Host "Creating database '$Database'..." -ForegroundColor Cyan
Invoke-PsqlCommand -DbName postgres -Sql "CREATE DATABASE $Database;"

Write-Host 'Creating _migration_history...' -ForegroundColor Cyan
Invoke-PsqlCommand -DbName $Database -Sql @"
CREATE TABLE IF NOT EXISTS _migration_history (
    name       text        PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);
"@

# ---- 3. Migrations and seeds ----

Write-Host ''
Write-Host "Applying migrations from $migrationsDir..." -ForegroundColor Cyan
$migrations = Get-ChildItem -Path $migrationsDir -Filter '*.sql' |
Where-Object { $_.Name -notlike 'seed*' } |
Sort-Object Name
if (-not $migrations) { throw "No migration files found in $migrationsDir." }
foreach ($m in $migrations) {
    Write-Host "  -> $($m.Name)" -ForegroundColor DarkGray
    Invoke-Migration -DbName $Database -File $m.FullName
}

Write-Host ''
Write-Host 'Seeding canon rosters...' -ForegroundColor Cyan
Invoke-PsqlFile -DbName $Database -File $seedCanon
if (Test-Path $seedTheme) {
    Write-Host 'Seeding OpenSCORM board theme...' -ForegroundColor Cyan
    Invoke-PsqlFile -DbName $Database -File $seedTheme
}

# ---- 4. Restore captured versions ----

foreach ($v in $captured) {
    Invoke-PsqlCommand -DbName $Database -Sql `
        "UPDATE app SET version_major = $($v.Major), version_minor = $($v.Minor), version_patch = $($v.Patch), updated_at = now() WHERE app_handle = '$($v.NewHandle)';"
}
Write-Host "Restored $($captured.Count) app versions." -ForegroundColor Cyan

# ---- 5. Admin account ----

if (-not [string]::IsNullOrWhiteSpace($AdminSeed)) {
    if (-not (Test-Path $AdminSeed)) { throw "Admin seed not found: $AdminSeed" }
    Write-Host 'Applying admin account seed...' -ForegroundColor Cyan
    Invoke-PsqlFile -DbName $Database -File $AdminSeed
}
else {
    Write-Warning 'No -AdminSeed provided; the database has no admin account until one is seeded.'
}

Write-Host ''
Write-Host "Database '$Database' recreated: $($migrations.Count) migrations, canon rosters, $($captured.Count) app versions." -ForegroundColor Green
