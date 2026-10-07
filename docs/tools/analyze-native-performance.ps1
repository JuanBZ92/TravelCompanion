[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$LogPath,
    [Parameter(Mandatory)][string]$Scenario,
    [string]$OutputDirectory = 'artifacts/native-performance-analysis',
    [string]$BuildConfiguration,
    [string]$DeviceModel,
    [string]$AppVersion,
    [string]$AppBuild,
    [int]$Warmups = 5,
    [int]$Repetitions = 30
)
$ErrorActionPreference = 'Stop'
if ($Warmups -lt 5 -or $Repetitions -lt 30) { throw 'Use at least 5 warmups and 30 measured samples.' }
if ($Scenario -notmatch '^[A-Za-z0-9_.-]{1,80}$') { throw 'Scenario must be a short code-defined label, without personal data.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$analysisOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
if (-not $analysisOutput.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Analysis output must stay inside this repository.'
}
$eventNames = @('journal_save_measured', 'expense_save_measured', 'journal_thumbnail_measured',
    'journal_photo_measured', 'preview_cleanup_measured', 'schedule_timer_measured')
$numericFields = @('elapsedTicks', 'tickFrequency', 'elapsedMs', 'allocatedBytes', 'requests', 'reads', 'bytesRead')
$parsed = [Collections.Generic.List[object]]::new()
$malformedLines = 0
$invalidNumericFields = 0
$inputCount = 0

function Read-NonNegativeNumber($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -isnot [ValueType] -or $Value -is [bool]) { return $null }
    try {
        $number = [double]$Value
        if ([double]::IsFinite($number) -and $number -ge 0) { return $number }
    } catch { }
    return $null
}

function Get-Percentile($Values, [double]$Fraction) {
    $valid = @($Values | Where-Object { $null -ne $_ } | Sort-Object)
    if ($valid.Count -eq 0) { return $null }
    $index = [Math]::Clamp([int][Math]::Ceiling($valid.Count * $Fraction) - 1, 0, $valid.Count - 1)
    return $valid[$index]
}

foreach ($path in $LogPath) {
    $inputCount++
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadLines($resolved)) {
        $lineNumber++
        $jsonStart = $line.IndexOf('{')
        if ($jsonStart -lt 0) { continue }
        # Ignore other logcat tags. Plain JSONL from DiagnosticJournal is also supported.
        if ($jsonStart -gt 0 -and $line.Substring(0, $jsonStart) -notmatch '\bYukuPerformance\b') { continue }
        try { $entry = $line.Substring($jsonStart) | ConvertFrom-Json -Depth 10 -ErrorAction Stop }
        catch { $malformedLines++; continue }
        if ($entry.eventName -notin $eventNames) { continue }
        $details = [ordered]@{}
        foreach ($field in $numericFields) {
            $value = Read-NonNegativeNumber $entry.details.$field
            if ($null -ne $entry.details.$field -and $null -eq $value) { $invalidNumericFields++ }
            $details[$field] = $value
        }
        $elapsed = $null
        if ($null -ne $details.elapsedTicks -and $details.tickFrequency -gt 0) {
            $elapsed = $details.elapsedTicks * 1000.0 / $details.tickFrequency
        } elseif ($null -ne $details.elapsedMs) { $elapsed = $details.elapsedMs }
        $utc = $null
        $dateValue = [DateTimeOffset]::MinValue
        if ($entry.utc -is [DateTimeOffset]) { $utc = $entry.utc.ToUniversalTime().ToString('O') }
        elseif ($entry.utc -is [DateTime]) { $utc = ([DateTimeOffset]$entry.utc).ToUniversalTime().ToString('O') }
        elseif ([DateTimeOffset]::TryParse([string]$entry.utc, [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None, [ref]$dateValue)) { $utc = $dateValue.ToUniversalTime().ToString('O') }
        $canceled = if ($entry.details.canceled -is [bool]) { $entry.details.canceled } else { $null }
        $parsed.Add([pscustomobject]@{
            Utc = $utc; Scenario = $Scenario; EventName = [string]$entry.eventName
            SourceIndex = $inputCount; LineNumber = $lineNumber
            Details = [pscustomobject]$details; ElapsedMs = $elapsed; Canceled = $canceled
        })
    }
}

New-Item -ItemType Directory -Path $analysisOutput -Force | Out-Null
$rawLines = @($parsed | ForEach-Object {
    [ordered]@{ utc = $_.Utc; scenario = $_.Scenario; eventName = $_.EventName; sourceIndex = $_.SourceIndex
        lineNumber = $_.LineNumber; details = $_.Details; canceled = $_.Canceled } | ConvertTo-Json -Depth 4 -Compress
})
[IO.File]::WriteAllLines((Join-Path $analysisOutput 'raw.jsonl'), [string[]]$rawLines)
$csvRows = [Collections.Generic.List[object]]::new()
$summaries = [Collections.Generic.List[object]]::new()
foreach ($eventName in $eventNames) {
    # Preserve capture order; callers must pass chronological, non-overlapping files from ONE scenario.
    $rows = @($parsed | Where-Object EventName -eq $eventName)
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $row = $rows[$i]
        $phase = if ($i -lt $Warmups) { 'warmup' } elseif ($i -lt $Warmups + $Repetitions) { 'measured' } else { 'extra' }
        $csvRows.Add([pscustomobject]@{
            Scenario = $Scenario; EventName = $eventName; SampleNumber = $i + 1; Phase = $phase; Utc = $row.Utc
            ElapsedMs = $row.ElapsedMs; ElapsedTicks = $row.Details.elapsedTicks; TickFrequency = $row.Details.tickFrequency
            AllocatedBytes = $row.Details.allocatedBytes; Requests = $row.Details.requests
            Reads = $row.Details.reads; BytesRead = $row.Details.bytesRead; Canceled = $row.Canceled
            AllocationScope = if ($eventName -in @('schedule_timer_measured', 'preview_cleanup_measured')) { 'current-thread' } else { 'process-global-interval' }
        })
    }
    $measured = @($rows | Select-Object -Skip $Warmups -First $Repetitions)
    $metricSummary = [ordered]@{}
    foreach ($metric in @('ElapsedMs', 'AllocatedBytes', 'Requests', 'Reads', 'BytesRead')) {
        $values = if ($metric -eq 'ElapsedMs') { @($measured | ForEach-Object { $_.ElapsedMs }) }
            else { @($measured | ForEach-Object { $_.Details.$metric }) }
        $validCount = @($values | Where-Object { $null -ne $_ }).Count
        $complete = $measured.Count -eq $Repetitions -and $validCount -eq $Repetitions
        $metricSummary[$metric] = [ordered]@{
            ValidCount = $validCount
            P50 = if ($complete) { Get-Percentile $values .5 } else { $null }
            P95 = if ($complete) { Get-Percentile $values .95 } else { $null }
            ObservedP50 = Get-Percentile $values .5
            ObservedP95 = Get-Percentile $values .95
        }
    }
    $summaries.Add([pscustomobject]@{
        Scenario = $Scenario; EventName = $eventName; CapturedCount = $rows.Count
        WarmupsExcluded = [Math]::Min($Warmups, $rows.Count); MeasuredCount = $measured.Count
        ExtraCount = [Math]::Max(0, $rows.Count - $Warmups - $Repetitions)
        SamplesComplete = $measured.Count -eq $Repetitions
        CanceledCount = @($measured | Where-Object Canceled -eq $true).Count
        CancellationUnknownCount = @($measured | Where-Object { $null -eq $_.Canceled }).Count
        AllocationScope = if ($eventName -in @('schedule_timer_measured', 'preview_cleanup_measured')) { 'current-thread' } else { 'process-global-interval' }
        Metrics = $metricSummary
    })
}
$csvRows | Export-Csv -LiteralPath (Join-Path $analysisOutput 'samples.csv') -NoTypeInformation -Encoding utf8
$report = [ordered]@{
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); Scenario = $Scenario
    BuildConfiguration = if ($BuildConfiguration) { $BuildConfiguration } else { $null }
    DeviceModel = if ($DeviceModel) { $DeviceModel } else { $null }
    AppVersion = if ($AppVersion) { $AppVersion } else { $null }
    AppBuild = if ($AppBuild) { $AppBuild } else { $null }
    InputCount = $inputCount; ParsedCount = $parsed.Count; MalformedLines = $malformedLines
    InvalidNumericFields = $invalidNumericFields; Warmups = $Warmups; Repetitions = $Repetitions
    Notes = 'Metadata only. Stopwatch ticks measure operation intervals, not CPU usage or total UI/HTTP latency. Allocation/request/read counters cover the process interval and may include overlapping work; schedule_timer and preview_cleanup allocations cover the current thread. Unknown counters remain null. P50/P95 require all 30 valid samples; ObservedP50/P95 describe incomplete captures only. No success/error attribution is available unless the capture explicitly provides it. Raw output contains only allowed metrics and code-defined event/scenario labels; original logcat is not copied.'
    Operations = $summaries
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $analysisOutput 'summary.json') -Encoding utf8
$summaries | Select-Object EventName, CapturedCount, MeasuredCount, SamplesComplete |
    Format-Table -AutoSize
