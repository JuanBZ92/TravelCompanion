[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Current,
    [Parameter(Mandatory)][string[]]$Operation,
    [string]$OutputDirectory = 'artifacts/native-performance-comparison'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$comparisonOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
if (-not $comparisonOutput.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Comparison output must stay inside this repository.'
}
$before = Get-Content -LiteralPath $Baseline -Raw | ConvertFrom-Json -Depth 12
$after = Get-Content -LiteralPath $Current -Raw | ConvertFrom-Json -Depth 12
foreach ($field in @('Scenario', 'Platform', 'DeviceModel', 'BuildConfiguration', 'Fixture', 'Warmups', 'Repetitions')) {
    if ($null -eq $before.$field -or [string]::IsNullOrWhiteSpace([string]$before.$field) -or
        $before.$field -cne $after.$field) { throw "Missing or mismatched comparison metadata: $field" }
}
if ($before.BuildConfiguration -cne 'Release' -or $before.Warmups -lt 5 -or $before.Repetitions -lt 30) {
    throw 'Compare native Release captures with at least five warmups and thirty repetitions.'
}
foreach ($report in @($before, $after)) {
    foreach ($field in @('ArtifactSha256', 'SourceSnapshotSha256')) {
        if ($report.$field -notmatch '^[A-Fa-f0-9]{64}$') { throw "Missing artifact/source identity: $field" }
    }
}
if ($Operation.Count -eq 0 -or @($Operation | Select-Object -Unique).Count -ne $Operation.Count) {
    throw 'Select each measured operation exactly once.'
}
$rows = [Collections.Generic.List[object]]::new()
foreach ($name in $Operation) {
    $old = @($before.Operations | Where-Object EventName -eq $name)
    $new = @($after.Operations | Where-Object EventName -eq $name)
    if ($old.Count -ne 1 -or $new.Count -ne 1 -or !$old[0].SamplesComplete -or !$new[0].SamplesComplete) {
        throw "Incomplete or missing measured operation: $name"
    }
    if ($old[0].CanceledCount -gt 0 -or $new[0].CanceledCount -gt 0) { throw "Canceled samples in $name" }
    $batchArea = switch ($name) {
        'bootstrap_refresh_measured' { 'bootstrap' }
        'schedule_load_measured' { 'trip' }
        'journal_local_open_measured' { 'journal' }
        'day_planning_measured' { 'planning' }
    }
    if ($batchArea) {
        foreach ($measuredOperation in @($old[0], $new[0])) {
            if ($measuredOperation.CapturedCount -ne $before.Warmups + $before.Repetitions -or $measuredOperation.ExtraCount -ne 0) {
                throw "Ambiguous native capture: expected exactly one interval per batch iteration for $name"
            }
        }
        foreach ($report in @($before, $after)) {
            $batch = @($report.Batches | Where-Object Operation -eq $batchArea)
            if ($batch.Count -ne 1 -or $batch[0].Status -ne 200 -or $batch[0].Canceled -or
                $batch[0].CompletedIterations -ne $report.Warmups + $report.Repetitions) {
                throw "Missing successful complete native batch: $batchArea"
            }
        }
    }
    foreach ($metric in @('ElapsedMs', 'AllocatedBytes', 'Requests', 'Reads', 'BytesRead', 'KnownResponseBytes', 'UnknownLengthResponses')) {
        foreach ($percentile in @('P50', 'P95')) {
            $baselineValue = $old[0].Metrics.$metric.$percentile
            $currentValue = $new[0].Metrics.$metric.$percentile
            $measured = $null -ne $baselineValue -and $null -ne $currentValue
            $change = if ($measured -and $baselineValue -gt 0) { 100 * ($currentValue - $baselineValue) / $baselineValue } else { $null }
            $rows.Add([pscustomobject]@{
                Operation = $name; Metric = $metric; Percentile = $percentile
                Baseline = $baselineValue; Current = $currentValue; ChangePercent = $change
                RequiresInvestigation = $measured -and (($null -ne $change -and $change -gt 10) -or
                    ($baselineValue -eq 0 -and $currentValue -gt 0))
                Comparable = $measured
            })
        }
    }
}
$report = [ordered]@{
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Scenario = $before.Scenario; Fixture = $before.Fixture; DeviceModel = $before.DeviceModel; Platform = $before.Platform
    BaselineArtifactSha256 = $before.ArtifactSha256; CurrentArtifactSha256 = $after.ArtifactSha256
    BaselineSourceSnapshotSha256 = $before.SourceSnapshotSha256; CurrentSourceSnapshotSha256 = $after.SourceSnapshotSha256
    SameArtifact = $before.ArtifactSha256 -ceq $after.ArtifactSha256
    Warmups = $before.Warmups; Repetitions = $before.Repetitions
    HasInvestigationCandidates = @($rows | Where-Object RequiresInvestigation).Count -gt 0
    Results = $rows.ToArray()
    Notes = 'Observed changes above 10% require a repeated comparable capture and investigation. This report is not a timing CI gate or proof of a reproducible regression. Missing metrics stay null; known HTTP lengths are partial when unknown-length responses exist. A same-artifact comparison measures repeatability, not an optimization. Caller-supplied metadata must match the actual device, fixture and files.'
}
New-Item -ItemType Directory -Path $comparisonOutput -Force | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $comparisonOutput 'comparison.json') -Encoding utf8
$rows | Export-Csv -LiteralPath (Join-Path $comparisonOutput 'comparison.csv') -NoTypeInformation -Encoding utf8
$rows | Where-Object { $_.Metric -eq 'ElapsedMs' -or $_.RequiresInvestigation } | Format-Table -AutoSize
