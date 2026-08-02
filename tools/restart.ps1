param(
    [switch]$ResetDb,
    [switch]$SkipWeb,
    [switch]$SkipWorker,
    [switch]$ApiOnly,
    [int]$ApiReadyTimeoutSeconds = 60,
    [int]$WebReadyTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

# Stop everything regardless of switches - a service excluded from the new
# start (e.g. -ApiOnly) should not keep running from the old stack.
& (Join-Path $PSScriptRoot 'stop.ps1')
Start-Sleep -Seconds 1
& (Join-Path $PSScriptRoot 'start.ps1') `
    -ResetDb:$ResetDb `
    -SkipWeb:$SkipWeb `
    -SkipWorker:$SkipWorker `
    -ApiOnly:$ApiOnly `
    -ApiReadyTimeoutSeconds $ApiReadyTimeoutSeconds `
    -WebReadyTimeoutSeconds $WebReadyTimeoutSeconds
