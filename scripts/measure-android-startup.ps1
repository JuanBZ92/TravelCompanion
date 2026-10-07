param(
    [Parameter(Mandatory)][string]$Device,
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$Label,
    [string]$Adb = 'C:/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe',
    [string]$OutputDirectory = 'artifacts/daily-ux-20261006',
    [int]$Warmups = 5,
    [int]$Repetitions = 30
)
$ErrorActionPreference = 'Stop'
# This runner force-stops only a separate review installation. It never clears app data.
if ($Package -notmatch '^com\.yuku\.travelcompanion\.[a-z]*review$' -or $Label -notmatch '^[a-z0-9-]+$') { throw 'Use a Yuku review package and a simple measurement label.' }
if ($Warmups -lt 0 -or $Repetitions -lt 1) { throw 'Invalid repetition count.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$activity = (& $Adb -s $Device shell cmd package resolve-activity --brief $Package | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $activity -notmatch ('^' + [regex]::Escape($Package) + '/')) { throw 'Review activity unavailable.' }
$rows = @()
for ($i = 0; $i -lt ($Warmups + $Repetitions); $i++) {
    & $Adb -s $Device shell am force-stop $Package
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop review app.' }
    $raw = & $Adb -s $Device shell am start -S -W -n $activity
    if ($LASTEXITCODE -ne 0 -or ($raw -join "`n") -notmatch 'Status: ok') { throw 'Review launch failed.' }
    Add-Content -LiteralPath (Join-Path $OutputDirectory "$Label-startup-raw.log") -Value $raw
    $total = [regex]::Match(($raw -join "`n"), 'TotalTime:\s*(\d+)')
    $wait = [regex]::Match(($raw -join "`n"), 'WaitTime:\s*(\d+)')
    if (!$wait.Success) { throw 'Android wait timing unavailable; do not record a zero.' }
    $totalMs = if ($total.Success) { [int]$total.Groups[1].Value } else { $null }
    $rows += [pscustomobject]@{ iteration = $i; warmup = $i -lt $Warmups; totalMs = $totalMs; waitMs = [int]$wait.Groups[1].Value }
    Start-Sleep -Milliseconds 500
}
$rows | Export-Csv -LiteralPath (Join-Path $OutputDirectory "$Label-startup.csv") -NoTypeInformation
$measured = @($rows | Where-Object { !$_.warmup } | Sort-Object waitMs)
$draw = @($measured | Where-Object { $null -ne $_.totalMs } | Sort-Object totalMs)
$summary = [pscustomobject]@{ label = $Label; package = $Package; device = $Device; warmups = $Warmups; repetitions = $Repetitions; scenario = 'process-cold-first-activity'; p50WaitMs = $measured[[int][math]::Ceiling($Repetitions * .50) - 1].waitMs; p95WaitMs = $measured[[int][math]::Ceiling($Repetitions * .95) - 1].waitMs; drawSamples = $draw.Count; p50DrawMs = if ($draw.Count) { $draw[[int][math]::Ceiling($draw.Count * .5) - 1].totalMs } else { $null }; p95DrawMs = if ($draw.Count) { $draw[[int][math]::Ceiling($draw.Count * .95) - 1].totalMs } else { $null }; scope = 'Android launch wait and reported first draw only; missing draw timing is null. Does not measure completed login, network or full screen readiness.' }
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory "$Label-startup-summary.json")
$summary | Format-List
