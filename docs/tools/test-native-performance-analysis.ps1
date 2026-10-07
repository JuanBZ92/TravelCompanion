[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/native-performance-analysis-tests')
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
if (-not $testOutput.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test output must stay inside this repository.'
}
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Operation($Report, [string]$Name) { return $Report.Operations | Where-Object EventName -eq $Name }
$names = @('journal_save_measured', 'expense_save_measured', 'journal_thumbnail_measured',
    'journal_photo_measured', 'preview_cleanup_measured', 'schedule_timer_measured')
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('10-06 14:00:00 I OtherTag: {"eventName":"journal_save_measured","details":{"elapsedMs":999}}')
$lines.Add('10-06 14:00:00 I YukuPerformance: {broken')
$lines.Add('{"eventName":"unhandled_exception","privatePayload":"synthetic-private-marker"}')
for ($i = 0; $i -lt 37; $i++) {
    foreach ($name in $names) {
        $ms = if ($i -lt 5) { 1000 } elseif ($i -lt 35) { $i - 4 + .125 } else { 999 }
        $details = [ordered]@{ elapsedTicks = $ms * 1000; tickFrequency = 1000000; elapsedMs = 0
            allocatedBytes = 100; canceled = $false; privatePayload = 'synthetic-private-marker' }
        if ($name -ne 'schedule_timer_measured') { $details.requests = 0; $details.reads = 6; $details.bytesRead = 8192 }
        $json = @{ utc = '2026-10-06T14:00:00Z'; eventName = $name; details = $details } | ConvertTo-Json -Depth 4 -Compress
        $lines.Add($(if ($i % 2 -eq 0) { "10-06 14:00:00 I YukuPerformance: $json" } else { $json }))
    }
}
$completeLog = Join-Path $testOutput 'complete.log'
[IO.File]::WriteAllLines($completeLog, $lines)
& (Join-Path $PSScriptRoot 'analyze-native-performance.ps1') -LogPath $completeLog -Scenario synthetic-complete `
    -OutputDirectory (Join-Path $OutputDirectory 'complete') -BuildConfiguration Release -DeviceModel synthetic | Out-Null
$completeDirectory = Join-Path $testOutput 'complete'
$report = Get-Content (Join-Path $completeDirectory 'summary.json') -Raw | ConvertFrom-Json
Assert-True ($report.ParsedCount -eq 222 -and $report.MalformedLines -eq 1) 'Malformed and unrelated records must be excluded.'
foreach ($name in $names) {
    $operation = Operation $report $name
    Assert-True ($operation.WarmupsExcluded -eq 5 -and $operation.MeasuredCount -eq 30 -and $operation.ExtraCount -eq 2) 'Sample phases are incorrect.'
    Assert-True ($operation.Metrics.ElapsedMs.P50 -eq 15.125 -and $operation.Metrics.ElapsedMs.P95 -eq 29.125) 'Ticks or percentile selection is incorrect.'
    $expectedScope = if ($name -in @('schedule_timer_measured', 'preview_cleanup_measured')) { 'current-thread' } else { 'process-global-interval' }
    Assert-True ($operation.AllocationScope -eq $expectedScope) 'Summary allocation scope must match the instrumentation.'
}
$timer = Operation $report 'schedule_timer_measured'
Assert-True ($null -eq $timer.Metrics.Requests.P50 -and $timer.Metrics.Requests.ValidCount -eq 0) 'Missing counters must remain null.'
$save = Operation $report 'journal_save_measured'
Assert-True ($save.Metrics.Requests.P50 -eq 0 -and $save.Metrics.Requests.ValidCount -eq 30) 'Recorded zero counters must remain zero.'
$csv = Import-Csv (Join-Path $completeDirectory 'samples.csv')
Assert-True (@($csv | Where-Object { $_.Phase -eq 'warmup' }).Count -eq 30) 'CSV warmup classification is incorrect.'
Assert-True (@($csv | Where-Object { $_.EventName -eq 'schedule_timer_measured' -and $_.Requests -ne '' }).Count -eq 0) 'Missing CSV counters must be blank.'
Assert-True (@($csv | Where-Object { $_.EventName -in @('schedule_timer_measured', 'preview_cleanup_measured') -and $_.AllocationScope -ne 'current-thread' }).Count -eq 0) 'Timer and cleanup CSV allocation scopes must be current-thread.'
Assert-True (@($csv | Where-Object { $_.EventName -notin @('schedule_timer_measured', 'preview_cleanup_measured') -and $_.AllocationScope -ne 'process-global-interval' }).Count -eq 0) 'Store CSV allocation scopes must remain process-global-interval.'
$raw = Get-Content (Join-Path $completeDirectory 'raw.jsonl') -Raw
Assert-True (-not $raw.Contains('synthetic-private-marker') -and -not $raw.Contains('privatePayload')) 'Filtered output leaked disallowed metadata.'
$first = (Get-Content (Join-Path $completeDirectory 'raw.jsonl') -TotalCount 1 | ConvertFrom-Json).utc
Assert-True (([DateTimeOffset]$first).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss') -eq '2026-10-06T14:00:00') 'UTC date changed during JSON conversion.'
$partialLines = @(1..8 | ForEach-Object {
    @{ eventName = 'journal_save_measured'; details = @{ elapsedMs = $_; allocatedBytes = -1; requests = '2'; reads = 0 } } |
        ConvertTo-Json -Depth 4 -Compress
})
$partialLog = Join-Path $testOutput 'partial.log'
[IO.File]::WriteAllLines($partialLog, [string[]]$partialLines)
& (Join-Path $PSScriptRoot 'analyze-native-performance.ps1') -LogPath $partialLog -Scenario synthetic-partial `
    -OutputDirectory (Join-Path $OutputDirectory 'partial') | Out-Null
$partial = Get-Content (Join-Path $testOutput 'partial/summary.json') -Raw | ConvertFrom-Json
$partialSave = Operation $partial 'journal_save_measured'
Assert-True ($partialSave.MeasuredCount -eq 3 -and $null -eq $partialSave.Metrics.ElapsedMs.P50 -and $partialSave.Metrics.ElapsedMs.ObservedP50 -eq 7) 'Partial samples must not become certified percentiles.'
Assert-True ($partial.InvalidNumericFields -eq 16 -and $partialSave.Metrics.AllocatedBytes.ValidCount -eq 0 -and $partialSave.Metrics.Requests.ValidCount -eq 0) 'Invalid negative or string metrics must be rejected.'
$missing = Operation $partial 'journal_photo_measured'
Assert-True ($missing.CapturedCount -eq 0 -and $null -eq $missing.Metrics.ElapsedMs.P50 -and $null -eq $missing.Metrics.ElapsedMs.ObservedP50) 'Absent operations must have null metrics.'
Assert-True ($null -eq $partial.AppVersion -and $null -eq $partial.DeviceModel) 'Unknown build metadata must remain null.'
Write-Output 'Native analysis smoke tests passed: phases, ticks, UTC, null versus zero, incomplete/invalid samples and metadata whitelist.'
