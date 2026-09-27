<#
.SYNOPSIS
    Lists or fetches Bump problem reports with a read key, for an unattended
    reader such as an agent checking whether production is faulting.

.DESCRIPTION
    Reads the key from a file and sends it as the bearer token, so the key
    never appears in the command line, the output, or the caller's session.
    A read key can list and fetch problems and nothing else: Bump answers 403
    if it is used to report, resolve or delete.

    Filters pass straight through to GET /api/problems, which answers 400
    (and this script throws) for an unknown parameter, app or environment,
    rather than an empty list.

.EXAMPLE
    ./tools/get-problems.ps1 -AppHandle slate -Environment live -Since 2026-09-20

.EXAMPLE
    ./tools/get-problems.ps1 -Fingerprint d86626ece845a70a -IncludeResolved

.EXAMPLE
    ./tools/get-problems.ps1 -Id 1234
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = $env:BUMP_API_BASEURL,
    [string] $KeyPath = 'C:\base\me\secrets\bump-read-engineering.key',
    [long] $Id,
    [string] $AppHandle,
    [string] $Environment,
    [string] $Fingerprint,
    [string] $Since,
    [string] $Until,
    [int] $Limit = 50,
    [switch] $IncludeResolved
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    throw 'Bump base URL required via -BaseUrl parameter or BUMP_API_BASEURL environment variable.'
}
if (-not (Test-Path -LiteralPath $KeyPath)) {
    throw "No read key at $KeyPath. Pass -KeyPath, or ask for a key from Bump:Api:Problems:ReadSecrets."
}

$key = (Get-Content -LiteralPath $KeyPath -Raw).Trim()
$headers = @{ Authorization = "Bearer $key" }
$root = $BaseUrl.TrimEnd('/') + '/api/problems'

if ($Id) {
    $uri = "$root/$Id"
}
else {
    $query = [ordered]@{ limit = $Limit; includeResolved = $IncludeResolved.IsPresent.ToString().ToLowerInvariant() }
    if ($AppHandle) { $query.appHandle = $AppHandle }
    if ($Environment) { $query.environment = $Environment }
    if ($Fingerprint) { $query.fingerprint = $Fingerprint }
    if ($Since) { $query.since = $Since }
    if ($Until) { $query.until = $Until }
    $pairs = foreach ($name in $query.Keys) { "$name=$([uri]::EscapeDataString([string]$query[$name]))" }
    $uri = "${root}?$($pairs -join '&')"
}

$response = Invoke-WebRequest -Uri $uri -Headers $headers -Method Get -SkipHttpErrorCheck
if ($response.StatusCode -ne 200) {
    throw "Bump answered $($response.StatusCode) for GET $uri`n$($response.Content)"
}

$response.Content
