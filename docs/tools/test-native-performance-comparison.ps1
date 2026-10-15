[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/native-performance-comparison-tests')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
if (-not $target.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Repository output only.' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
function Assert-True([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
$operation = 'journal_local_open_measured'
$metrics = [ordered]@{}
foreach ($metric in @('ElapsedMs', 'AllocatedBytes', 'Requests', 'Reads', 'BytesRead', 'KnownResponseBytes', 'UnknownLengthResponses')) {
    $metrics[$metric] = @{ P50 = 100; P95 = 100; ValidCount = 30 }
}
$base = [ordered]@{ Scenario = 'journal-250-local'; Platform = 'Android'; DeviceModel = 'synthetic'; BuildConfiguration = 'Release'
    Fixture = 'synthetic-v1'; Warmups = 5; Repetitions = 30; ArtifactSha256 = 'A' * 64; SourceSnapshotSha256 = 'B' * 64
    Batches = @(@{ Operation = 'journal'; Status = 200; CompletedIterations = 35; Canceled = $false })
    Operations = @(@{ EventName = $operation; SamplesComplete = $true; CanceledCount = 0; CapturedCount = 35; ExtraCount = 0; Metrics = $metrics }) }
$basePath = Join-Path $target 'baseline.json'; $currentPath = Join-Path $target 'current.json'
$base | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $basePath
function Current { $base | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable -Depth 10 }
function Run($Report) {
    $Report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $currentPath
    & (Join-Path $PSScriptRoot 'compare-native-performance.ps1') -Baseline $basePath -Current $currentPath `
        -Operation $operation -OutputDirectory (Join-Path $OutputDirectory 'comparison') | Out-Null
}
function Reject($Report, [string]$Message) {
    $rejected = $false
    try { Run $Report } catch { $rejected = $true }
    Assert-True $rejected $Message
}
$current = Current; $current.ArtifactSha256 = 'C' * 64
$current.Operations[0].Metrics.ElapsedMs.P95 = 111
$current.Operations[0].Metrics.AllocatedBytes.P50 = 50
$current.Operations[0].Metrics.Reads.P50 = 0; $base.Operations[0].Metrics.Reads.P50 = 0
$base | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $basePath
Run $current
$report = Get-Content (Join-Path $target 'comparison/comparison.json') -Raw | ConvertFrom-Json -Depth 10
Assert-True ($report.HasInvestigationCandidates -and !$report.SameArtifact) 'A regression above 10% must require investigation.'
Assert-True (@($report.Results | Where-Object RequiresInvestigation).Count -eq 1) 'Only the regressed percentile should be flagged.'
$current = Current; $current.DeviceModel = 'different'; Reject $current 'Mismatched devices must be rejected.'
$current = Current; $current.Fixture = 'different'; Reject $current 'Mismatched fixtures must be rejected.'
$current = Current; $current.ArtifactSha256 = $null; Reject $current 'Missing artifact identity must be rejected.'
$current = Current; $current.Operations[0].SamplesComplete = $false; Reject $current 'Partial samples must be rejected.'
$current = Current; $current.Batches[0].Status = 500; Reject $current 'Failed batches must be rejected even when 35 scope events exist.'
$current = Current; $current.Batches[0].CompletedIterations = 34; Reject $current 'Incomplete batches must be rejected.'
$current = Current; $current.Operations[0].CapturedCount = 36; $current.Operations[0].ExtraCount = 1; Reject $current 'Overlapping extra intervals must be rejected.'
$current = Current; $current.Operations[0].Metrics.AllocatedBytes.P50 = $null; Run $current
$report = Get-Content (Join-Path $target 'comparison/comparison.json') -Raw | ConvertFrom-Json -Depth 10
$missing = $report.Results | Where-Object { $_.Metric -eq 'AllocatedBytes' -and $_.Percentile -eq 'P50' }
Assert-True (!$missing.Comparable -and $null -eq $missing.ChangePercent) 'Missing values must not become measured zeroes.'
Write-Output 'Native comparison tests passed: provenance, fixtures, complete batches, regressions and missing values.'
