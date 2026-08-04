# Exports the live database schema to db/schema.sql (the starting schema for
# new environments) and regenerates the ER diagram (db/schema.dot + db/schema.svg).
# Requires pg_dump and psql on PATH; the SVG step requires GraphViz (dot).
[CmdletBinding()]
param(
    [string] $Database = 'bump',
    [string] $Server = 'localhost',
    [int]    $Port = 5432,
    [string] $Username = 'postgres',
    [string] $Password = $env:PGPASSWORD,
    [string] $OutputPath = (Join-Path $PSScriptRoot 'schema.sql')
)

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw 'Password required via -Password parameter or PGPASSWORD environment variable.'
}

# psql, pg_dump, and pg_restore all read the password from PGPASSWORD. The
# variable stays set in the caller's session afterward: it is the developer's
# own local password, and every db script in every repo reads the same source.
$env:PGPASSWORD = $Password

& pg_dump `
    --host=$Server `
    --port=$Port `
    --username=$Username `
    --dbname=$Database `
    --schema-only `
    --create `
    --no-owner `
    --no-privileges `
    --no-tablespaces `
    --file=$OutputPath

if ($LASTEXITCODE -ne 0) {
    throw "pg_dump exited with code $LASTEXITCODE"
}
Write-Host "Wrote $OutputPath"

# ---- ER diagram (crow's foot): tables as nodes, foreign keys as edges ----

$baseArgs = @('-h', $Server, '-p', $Port, '-U', $Username, '-d', $Database,
              '--no-psqlrc', '--quiet', '--tuples-only', '--no-align', '--field-separator=|')

# Every user schema, not just public: hub keeps its integration tables in a
# separate schema, and openscorm spreads across security/progress/billing/etc.
# Skip the two catalog schemas and infrastructure tables (leading underscore,
# e.g. _migration_history). Rows come back as schema|table.
$tables = & psql @baseArgs -c @"
SELECT table_schema, table_name FROM information_schema.tables
WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
  AND table_type = 'BASE TABLE' AND table_name NOT LIKE '\_%'
ORDER BY table_schema, table_name;
"@
if ($LASTEXITCODE -ne 0) { throw "psql exited with code $LASTEXITCODE (table list)" }

# Cross-schema foreign keys count too, so carry the schema on both ends. Rows
# come back as childSchema|child|parentSchema|parent|deleteRule.
$fks = & psql @baseArgs -c @"
SELECT DISTINCT tc.table_schema, tc.table_name, ccu.table_schema, ccu.table_name, rc.delete_rule
FROM information_schema.table_constraints tc
JOIN information_schema.referential_constraints rc ON tc.constraint_name = rc.constraint_name AND tc.constraint_schema = rc.constraint_schema
JOIN information_schema.constraint_column_usage ccu ON rc.unique_constraint_name = ccu.constraint_name AND rc.unique_constraint_schema = ccu.constraint_schema
WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
ORDER BY 1, 2, 3, 4;
"@
if ($LASTEXITCODE -ne 0) { throw "psql exited with code $LASTEXITCODE (foreign keys)" }

# public is the default schema, so its tables stay bare (e.g. owner) to keep
# single-schema diagrams unchanged; anything else is qualified and quoted
# ("security.account"), since a dotted id is not a bare DOT identifier.
function Format-Node {
    param([string] $Schema, [string] $Table)
    if ($Schema -eq 'public') { return $Table }
    return '"' + $Schema + '.' + $Table + '"'
}

$dotPath = Join-Path $PSScriptRoot 'schema.dot'
$svgPath = Join-Path $PSScriptRoot 'schema.svg'

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('/*')
$lines.Add(" * $Database schema ER diagram (crow's foot notation). GENERATED FILE -")
$lines.Add(' * regenerate with db/export-schema.ps1; do not edit by hand.')
$lines.Add(' *')
$lines.Add(' * Edges go child -> parent: crow = many (child side), tee = one (parent')
$lines.Add(' * side). Non-CASCADE delete rules are labelled.')
$lines.Add(' */')
$lines.Add("digraph ${Database}_schema {")
$lines.Add('    rankdir=LR;')
$lines.Add('    bgcolor="white";')
$lines.Add('    node [shape=box, style="filled,rounded", fillcolor="#f5f5f5", fontname="Helvetica", fontsize=11, color="#444444"];')
$lines.Add('    edge [fontname="Helvetica", fontsize=8, color="#555555", dir=both, arrowsize=0.9];')
$lines.Add('')
foreach ($t in ($tables | Where-Object { $_ })) {
    $parts = $t.Split('|')
    $node = Format-Node -Schema $parts[0].Trim() -Table $parts[1].Trim()
    $lines.Add("    $node;")
}
$lines.Add('')
foreach ($row in ($fks | Where-Object { $_ })) {
    $parts = $row.Split('|')
    $child = Format-Node -Schema $parts[0].Trim() -Table $parts[1].Trim()
    $parent = Format-Node -Schema $parts[2].Trim() -Table $parts[3].Trim()
    $rule = $parts[4].Trim()
    $label = if ($rule -ne 'CASCADE') { " label=`"$rule`"" } else { '' }
    $lines.Add("    $child -> $parent [arrowtail=crow, arrowhead=tee$label];")
}
$lines.Add('}')
Set-Content -Path $dotPath -Value ($lines -join "`n") -Encoding utf8
Write-Host "Wrote $dotPath"

if (Get-Command dot -ErrorAction SilentlyContinue) {
    & dot -Tsvg $dotPath -o $svgPath
    if ($LASTEXITCODE -ne 0) { throw "dot exited with code $LASTEXITCODE" }
    Write-Host "Wrote $svgPath"
}
else {
    Write-Warning "GraphViz (dot) not found on PATH; skipped $svgPath."
}
