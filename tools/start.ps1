param(
    [switch]$ResetDb,
    [switch]$SkipWeb,
    [switch]$SkipWorker,
    [switch]$ApiOnly,
    [int]$ApiReadyTimeoutSeconds = 60,
    [int]$WebReadyTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$apiProj    = Join-Path $repoRoot 'src\Bump.Api\Bump.Api.csproj'
$workerProj = Join-Path $repoRoot 'src\Bump.Worker\Bump.Worker.csproj'
$webDir     = Join-Path $repoRoot 'web'
$resetScript = Join-Path $PSScriptRoot 'reset-database.ps1'

# Logs under tmp/logs/<subsystem>/ (one directory per subsystem), pids under
# tmp/pids/. Both gitignored via tmp/; tools/ stays clean of runtime files.
$logDir = Join-Path $repoRoot 'tmp\logs'
$pidDir = Join-Path $repoRoot 'tmp\pids'
foreach ($d in @("$logDir\api", "$logDir\worker", "$logDir\web", $pidDir)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

$services = @(
    @{
        Name = 'Bump.Api'
        Port = 5135
        Pid  = Join-Path $pidDir 'api.pid'
        Out  = Join-Path $logDir 'api\out.log'
        Err  = Join-Path $logDir 'api\err.log'
        File = 'dotnet'
        # No --urls override: the port comes from Bump:Api:Hosting:Urls in
        # config/appsettings.work.json, so there is one place to change it.
        Args = @('run', '--project', $apiProj, '--launch-profile', 'Bump.Api')
        Cwd  = $repoRoot
        Wait = $true
    },
    @{
        Name = 'Bump.Worker'
        Port = 8080
        Pid  = Join-Path $pidDir 'worker.pid'
        Out  = Join-Path $logDir 'worker\out.log'
        Err  = Join-Path $logDir 'worker\err.log'
        File = 'dotnet'
        Args = @('run', '--project', $workerProj)
        Cwd  = $repoRoot
        Wait = $false
        Skip = $SkipWorker -or $ApiOnly
    },
    @{
        Name = 'Bump.Web'
        Port = 5173
        Pid  = Join-Path $pidDir 'web.pid'
        Out  = Join-Path $logDir 'web\out.log'
        Err  = Join-Path $logDir 'web\err.log'
        File = 'npm.cmd'
        Args = @('run', 'dev')
        Cwd  = $webDir
        Wait = $true
        Skip = $SkipWeb -or $ApiOnly
    }
)

function Test-Alive {
    param([string]$PidFile)
    if (-not (Test-Path $PidFile)) { return $false }
    $procId = Get-Content $PidFile -ErrorAction SilentlyContinue
    if (-not $procId) { return $false }
    return [bool](Get-Process -Id $procId -ErrorAction SilentlyContinue)
}

function Wait-PortReady {
    param([int]$Port, [int]$TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $client = New-Object System.Net.Sockets.TcpClient
            $task = $client.ConnectAsync('localhost', $Port)
            if ($task.Wait(1000) -and $client.Connected) {
                $client.Close()
                return $true
            }
            $client.Close()
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

if ($ResetDb) {
    Write-Host "Resetting database..." -ForegroundColor Cyan
    & $resetScript
}

Write-Host "Checking postgres on localhost:5432..." -ForegroundColor Cyan
$pg = Test-NetConnection -ComputerName localhost -Port 5432 -WarningAction SilentlyContinue
if (-not $pg.TcpTestSucceeded) {
    Write-Warning "Postgres not reachable on localhost:5432. Start it before continuing."
    exit 1
}

if (-not $SkipWeb -and -not $ApiOnly) {
    if (-not (Test-Path (Join-Path $webDir 'node_modules'))) {
        Write-Host "Installing web deps (first run)..." -ForegroundColor Cyan
        Push-Location $webDir
        npm install
        Pop-Location
        if ($LASTEXITCODE -ne 0) { throw "npm install failed." }
    }
}

Write-Host ""
foreach ($svc in $services) {
    if ($svc.Skip) { continue }

    if (Test-Alive $svc.Pid) {
        $existing = Get-Content $svc.Pid
        Write-Host "$($svc.Name) already running (PID $existing)." -ForegroundColor DarkGray
        continue
    }
    if (Test-Path $svc.Pid) { Remove-Item $svc.Pid -Force }

    Write-Host "Starting $($svc.Name)..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $svc.File `
        -ArgumentList $svc.Args `
        -WorkingDirectory $svc.Cwd `
        -RedirectStandardOutput $svc.Out `
        -RedirectStandardError $svc.Err `
        -WindowStyle Hidden `
        -PassThru
    $proc.Id | Out-File -FilePath $svc.Pid -Encoding ascii
    Write-Host "  PID $($proc.Id) port $($svc.Port) logs $($svc.Out)" -ForegroundColor DarkGray

    if ($svc.Wait) {
        $timeout = if ($svc.Name -eq 'Bump.Api') { $ApiReadyTimeoutSeconds } else { $WebReadyTimeoutSeconds }
        Write-Host "  waiting for :$($svc.Port)..." -ForegroundColor DarkGray
        if (Wait-PortReady -Port $svc.Port -TimeoutSeconds $timeout) {
            Write-Host "  ready" -ForegroundColor Green
        } else {
            Write-Warning "  $($svc.Name) did not bind :$($svc.Port) within ${timeout}s. Check $($svc.Err)."
        }
    }
}

Write-Host ""
Write-Host "URLs:" -ForegroundColor Green
Write-Host "  API     http://localhost:5135"
Write-Host "  Swagger http://localhost:5135/swagger"
if (-not ($SkipWorker -or $ApiOnly)) { Write-Host "  Worker  http://localhost:8080" }
if (-not ($SkipWeb    -or $ApiOnly)) { Write-Host "  Web     http://localhost:5173" }
Write-Host ""
Write-Host "Logs in $logDir" -ForegroundColor DarkGray
Write-Host "  Get-Content '$logDir\api\out.log' -Wait" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Stop with: .\tools\stop.ps1" -ForegroundColor DarkGray
