[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Device,
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][ValidateSet('bootstrap', 'trip', 'journal', 'planning')][string]$Operation,
    [Parameter(Mandatory)][string]$ArtifactPath,
    [Parameter(Mandatory)][string]$SourceSnapshotPath,
    [Parameter(Mandatory)][string]$Fixture,
    [Parameter(Mandatory)][string]$Label,
    [string]$Adb = 'C:/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe',
    [string]$OutputDirectory = 'artifacts/android-operation-performance',
    [int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
if ($Package -notmatch '^com\.yuku\.travelcompanion\.[a-z]*review$') { throw 'Only a separate Yuku review installation is supported.' }
if ($Device -notmatch '^[A-Za-z0-9._:-]+$' -or $Label -notmatch '^[a-z0-9-]+$' -or $Fixture -notmatch '^[a-z0-9-]+$') {
    throw 'Use constant device/fixture/label codes without personal data.'
}
if ($TimeoutSeconds -lt 10 -or $TimeoutSeconds -gt 300) { throw 'Timeout must be 10–300 seconds.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
if (-not $output.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Repository output only.' }
if (!(Test-Path -LiteralPath $ArtifactPath -PathType Leaf) -or !(Test-Path -LiteralPath $SourceSnapshotPath -PathType Leaf)) { throw 'Preserve the actual APK and source manifest first.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$rawPath = Join-Path $output "$Label-$Operation.jsonl"
if (Test-Path -LiteralPath $rawPath) { throw 'Capture exists; use a new label to preserve previous evidence.' }
$activity = (& $Adb -s $Device shell cmd package resolve-activity --brief $Package | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $activity -notmatch ('^' + [regex]::Escape($Package) + '/')) { throw 'Review activity unavailable.' }
$model = ((& $Adb -s $Device shell getprop ro.product.model) -join '').Trim()
$packageInfo = (& $Adb -s $Device shell dumpsys package $Package) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Could not read installed review metadata.' }
$build = [regex]::Match($packageInfo, 'versionCode=(\d+)').Groups[1].Value
$version = [regex]::Match($packageInfo, 'versionName=([^\r\n]+)').Groups[1].Value.Trim()
if (!$build -or !$version -or !$model) { throw 'Installed device/build metadata unavailable.' }
$capture = $null; $completed = $false
try {
    # The app must already be unlocked by PIN and foreground on a main tab.
    # Runtime policy also requires diagnostics enabled and an API at loopback.
    $capture = Start-Process -FilePath $Adb -WindowStyle Hidden -PassThru `
        -ArgumentList @('-s', $Device, 'logcat', '-v', 'raw', '-T', '1', '-s', 'YukuPerformance:I', '*:S') `
        -RedirectStandardOutput $rawPath -RedirectStandardError (Join-Path $output "$Label-$Operation-stderr.log")
    Start-Sleep -Milliseconds 300
    $launch = & $Adb -s $Device shell am start -n $activity --es yuku_performance_scenario $Operation
    if ($LASTEXITCODE -ne 0) { throw 'Could not dispatch the opt-in review measurement.' }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $batchStarted = $false; $result = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        # Start-Process keeps the redirected writer open. Share that handle while
        # polling; a partial trailing JSON line is ignored until the next poll.
        $stream = [IO.FileStream]::new($rawPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = [IO.StreamReader]::new($stream)
        try { $captured = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($line in $captured.Split("`n")) {
            if (!$line.StartsWith('{')) { continue }
            try { $entry = $line | ConvertFrom-Json -Depth 6 } catch { continue }
            if ($entry.details.area -ne $Operation) { continue }
            if ($entry.eventName -eq 'review_batch_started_measured') { $batchStarted = $true }
            elseif ($batchStarted -and $entry.eventName -eq 'review_batch_completed_measured') { $result = $entry; break }
        }
        if ($result) { break }
        if ($capture.HasExited) { throw 'ADB log capture stopped before the batch completed.' }
        Start-Sleep -Milliseconds 500
    }
    if (!$result) { throw 'No completed review batch. Check PIN, foreground app, diagnostics flag and loopback API; no missing samples are invented.' }
    $completed = $result.details.status -eq 200 -and $result.details.eventId -eq 35
    if (!$completed) { throw "Review batch did not finish all 35 iterations (status $($result.details.status)). Keep partial evidence." }
} finally {
    if ($capture -and !$capture.HasExited) { $capture.Kill(); $capture.WaitForExit() }
    # On timeout/cancellation the app's batch deadline/OnStop cancels work and restores its window flag.
    # This script never changes display settings, authentication or app data.
}
& (Join-Path $repo 'docs/tools/analyze-native-performance.ps1') -LogPath $rawPath -BatchOperation $Operation `
    -Scenario "$Operation-$Fixture" -Fixture $Fixture -OutputDirectory (Join-Path $OutputDirectory "$Label-$Operation-analysis") `
    -ArtifactPath $ArtifactPath -SourceSnapshotPath $SourceSnapshotPath -Platform Android -BuildConfiguration Release `
    -DeviceModel $model -AppBuild $build -AppVersion $version
$summary = Get-Content -LiteralPath (Join-Path $output "$Label-$Operation-analysis/summary.json") -Raw | ConvertFrom-Json -Depth 10
$expectedEvent = switch ($Operation) {
    'bootstrap' { 'bootstrap_refresh_measured' }
    'trip' { 'schedule_load_measured' }
    'journal' { 'journal_local_open_measured' }
    'planning' { 'day_planning_measured' }
}
$samples = $summary.Operations | Where-Object EventName -eq $expectedEvent
if (!$samples.SamplesComplete -or $samples.CapturedCount -ne 35 -or $samples.ExtraCount -ne 0) {
    throw 'The batch did not capture exactly 35 operation intervals. Preserve evidence and repeat after background loads finish.'
}
