param(
    [string]$Version,
    [string]$OctopusProject = "Bump",
    [string]$Environment = "live",
    [string]$Tenant = "djm",
    [switch]$SkipDeploy
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# Derive version as "<prefix>.<commitCount>" so every commit gets a unique,
# monotonic patch number. Prefix (major.minor) lives in build/version-prefix.txt
# so a rebase, squash, or convention shift can bump the prefix in-repo without
# editing this script. Override the whole value with -Version when needed.
if (-not $Version) {
    $prefixPath = Join-Path $PSScriptRoot 'version-prefix.txt'
    if (-not (Test-Path $prefixPath)) {
        throw "Version prefix file not found: $prefixPath"
    }
    $prefix = (Get-Content -Path $prefixPath -TotalCount 1).Trim()
    if (-not $prefix) {
        throw "Version prefix file is empty: $prefixPath"
    }
    $commitCount = (& git -C $repoRoot rev-list --count HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $commitCount) {
        throw "Could not derive version from git commit count (git rev-list exit=$LASTEXITCODE)."
    }
    $Version = "$prefix.$commitCount"
}
$solution = Join-Path $repoRoot 'Bump.sln'
$distDir = Join-Path $repoRoot 'dist'
$webDir = Join-Path $repoRoot 'web'
$apiDir = Join-Path $repoRoot 'src\Bump.Api'
$wwwroot = Join-Path $apiDir 'wwwroot'

$projects = @(
    @{ Name = 'Bump.Api'; Path = Join-Path $repoRoot 'src\Bump.Api\Bump.Api.csproj' },
    # @{ Name = 'Bump.Sdk'; Path = Join-Path $repoRoot 'src\Bump.Sdk\Bump.Sdk.csproj' },
    @{ Name = 'Bump.Worker'; Path = Join-Path $repoRoot 'src\Bump.Worker\Bump.Worker.csproj' }
)

if (Test-Path $distDir) {
    Remove-Item $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir | Out-Null

Write-Host "Building web bundle ($Version)..." -ForegroundColor Cyan
Push-Location $webDir
try {
    npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed." }
    # APP_VERSION is read by vite.config.ts and baked into __APP_VERSION__ so the SPA
    # footer matches the release version instead of the stale package.json.
    $env:APP_VERSION = $Version
    try {
        npm run build --silent
        if ($LASTEXITCODE -ne 0) { throw "vite build failed." }
    }
    finally {
        Remove-Item Env:APP_VERSION -ErrorAction SilentlyContinue
    }
}
finally {
    Pop-Location
}

# Stage SPA into Api wwwroot so dotnet publish picks it up as static content.
if (Test-Path $wwwroot) {
    Remove-Item $wwwroot -Recurse -Force
}
New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $webDir 'dist\*') -Destination $wwwroot -Recurse -Force

Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore $solution --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

foreach ($project in $projects) {
    $name = $project.Name
    $csproj = $project.Path

    if (-not (Test-Path $csproj)) {
        throw "Project not found: $csproj"
    }

    $publishDir = Join-Path $distDir "$name\publish"
    $zipPath = Join-Path $distDir "$name.$Version.zip"

    Write-Host ""
    Write-Host "Building $name $Version..." -ForegroundColor Cyan

    dotnet publish $csproj `
        --configuration Release `
        --output $publishDir `
        --no-restore `
        --verbosity quiet `
        -p:Version=$Version

    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $name." }

    # Stamp Release:Version into the published appsettings.json so the About page and the
    # probe user agent report the release that was actually installed. Without this the key
    # is hand-maintained and drifts from the assembly version on the very next build.
    $publishedSettings = Join-Path $publishDir 'appsettings.json'
    if (-not (Test-Path $publishedSettings)) {
        throw "Published appsettings.json not found for ${name}: $publishedSettings"
    }
    $settings = Get-Content -Raw -LiteralPath $publishedSettings | ConvertFrom-Json
    if (-not $settings.Release) {
        throw "Published appsettings.json for $name has no Release section to stamp."
    }
    $settings.Release.Version = $Version
    $settings | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $publishedSettings -Encoding utf8
    Write-Host "  Stamped Release:Version = $Version" -ForegroundColor DarkGray

    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath

    Remove-Item $publishDir -Recurse -Force

    $size = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
    Write-Host "  -> $zipPath ($size KB)" -ForegroundColor Green

    $SecretsPath = "c:\base\me\secrets"
    $env:OCTOPUS_URL = (Get-Content -Path $SecretsPath\threadwork-octo-url.txt -TotalCount 1).Trim()
    $env:OCTOPUS_API_KEY = (Get-Content -Path $SecretsPath\threadwork-octo-key.txt -TotalCount 1).Trim()
    $env:OCTOPUS_SPACE = "Threadwork"

    octopus package upload --package $zipPath --overwrite-mode OverwriteExisting
    if ($LASTEXITCODE -ne 0) { throw "octopus package upload failed with exit code $LASTEXITCODE" }
}

Write-Host ""
Write-Host "Done. Packages:" -ForegroundColor Cyan
Get-ChildItem "$distDir\*.zip" | ForEach-Object { Write-Host "  $($_.Name)" }

if ($SkipDeploy) {
    Write-Host ""
    Write-Host "SkipDeploy set — release/deploy stage skipped." -ForegroundColor Yellow
    return
}

Write-Host ""
Write-Host "Creating release $Version on project $OctopusProject..." -ForegroundColor Cyan
octopus release create --project $OctopusProject --version $Version --ignore-existing --no-prompt
if ($LASTEXITCODE -ne 0) { throw "octopus release create failed with exit code $LASTEXITCODE" }

Write-Host ""
Write-Host "Deploying $Version to $Environment..." -ForegroundColor Cyan
octopus release deploy --project $OctopusProject --version $Version --environment $Environment --tenant $Tenant --no-prompt
if ($LASTEXITCODE -ne 0) { throw "octopus release deploy failed with exit code $LASTEXITCODE" }

Write-Host ""
Write-Host "Deployed $OctopusProject $Version to $Environment." -ForegroundColor Green
