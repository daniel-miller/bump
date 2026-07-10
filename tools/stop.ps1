param(
    [int[]]$Ports = @(5135, 8080, 5173)
)

$ErrorActionPreference = 'Continue'

$labels = @{
    5135 = 'Bump.Api'
    8080 = 'Bump.Worker'
    5173 = 'Bump.Web'
}

$pidDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'tmp\pid'

$pidFiles = @{
    5135 = Join-Path $pidDir 'api.pid'
    8080 = Join-Path $pidDir 'worker.pid'
    5173 = Join-Path $pidDir 'web.pid'
}

foreach ($port in $Ports) {
    $label = if ($labels.ContainsKey($port)) { $labels[$port] } else { "port $port" }
    $pidFile = $pidFiles[$port]
    $killed = $false

    # Tree-kill from PID file first — dotnet run spawns a child process that
    # owns the port, and Stop-Process by PID alone leaves the child running.
    if ($pidFile -and (Test-Path $pidFile)) {
        $procId = Get-Content $pidFile -ErrorAction SilentlyContinue
        if ($procId -and (Get-Process -Id $procId -ErrorAction SilentlyContinue)) {
            Write-Host "Stopping $label (PID $procId, tree-kill)..." -ForegroundColor Cyan
            & taskkill.exe /T /F /PID $procId 2>$null | Out-Null
            $killed = $true
        }
        Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    }

    # Fall back to port lookup in case the PID file is stale or the
    # service was started outside start.ps1.
    $pids = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique

    if ($pids) {
        foreach ($processId in $pids) {
            $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
            $name = if ($proc) { $proc.ProcessName } else { 'unknown' }
            & taskkill.exe /T /F /PID $processId 2>$null | Out-Null
            Write-Host "Stopped $label ($port) — pid $processId ($name)" -ForegroundColor Green
            $killed = $true
        }
    }

    if (-not $killed) {
        Write-Host "$label ($port) not running." -ForegroundColor DarkGray
    }
}
