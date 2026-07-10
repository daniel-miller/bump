param(
    [string]$DbHost = 'localhost',
    [int]$Port = 5432,
    [string]$User = 'postgres',
    # Placeholder default: pass -Password with your local postgres password.
    [string]$Password = 'YOUR_LOCAL_PASSWORD',
    [string]$Database = 'bump'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dbDir = Join-Path $repoRoot 'db\migrations'
$reseed = Join-Path $PSScriptRoot 'reset.sql'

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
    # No --single-transaction: DROP/CREATE DATABASE cannot run inside a tx,
    # and single-statement -c calls are already implicitly wrapped by psql.
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

Write-Host "Dropping database '$Database'..." -ForegroundColor Cyan
Invoke-PsqlCommand -DbName postgres -Sql "DROP DATABASE IF EXISTS $Database WITH (FORCE);"

Write-Host "Creating database '$Database'..." -ForegroundColor Cyan
Invoke-PsqlCommand -DbName postgres -Sql "CREATE DATABASE $Database;"

Write-Host "Creating _migration_history..." -ForegroundColor Cyan
Invoke-PsqlCommand -DbName $Database -Sql @"
CREATE TABLE IF NOT EXISTS _migration_history (
    name       text        PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);
"@

Write-Host ""
Write-Host "Applying migrations from $dbDir..." -ForegroundColor Cyan

$migrations = Get-ChildItem -Path $dbDir -Filter '*.sql' |
Where-Object { $_.Name -notlike 'seed*' } |
Sort-Object Name

if (-not $migrations) {
    throw "No migration files found in $dbDir."
}

foreach ($m in $migrations) {
    Write-Host "  -> $($m.Name)" -ForegroundColor DarkGray
    Invoke-Migration -DbName $Database -File $m.FullName
}

Write-Host ""
if (Test-Path $reseed) {
    Write-Host "Reseeding from reset.sql..." -ForegroundColor Cyan
    Invoke-PsqlFile -DbName $Database -File $reseed
}
else {
    Write-Warning "reset.sql not found at $reseed - skipping reseed step."
}

Write-Host ""
Write-Host "Database recreated, $($migrations.Count) migrations applied." -ForegroundColor Green
