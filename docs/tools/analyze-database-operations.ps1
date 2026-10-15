[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LogPath,
    [string]$OutputDirectory = 'artifacts/database-operation-analysis',
    [switch]$UseLastBatch,
    [int]$Warmups = 5,
    [int]$Repetitions = 30
)
$ErrorActionPreference = 'Stop'
if ($Warmups -lt 5 -or $Repetitions -lt 30) { throw 'Use at least five warmups and thirty measured operations.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
if (-not $target.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Repository output only.' }
$operations = @('bootstrap', 'today', 'planning', 'day_planning', 'analytics.ingest', 'analytics.retention', 'analytics.retention.batch', 'purchases.reconcile')
$metrics = @('TotalMs', 'Commands', 'CommandMs', 'ConnectionMs', 'Rows', 'ProcessAgeSeconds')
$rows = [Collections.Generic.List[object]]::new()
$number = '(?<value>[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)'
$stream = [IO.FileStream]::new((Resolve-Path -LiteralPath $LogPath).Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
$reader = [IO.StreamReader]::new($stream)
try { while ($null -ne ($line = $reader.ReadLine())) {
    $match = [regex]::Match($line, 'Database operation (?<operation>[a-z_.]+): ')
    if (!$match.Success -or $match.Groups['operation'].Value -notin $operations) { continue }
    $record = [ordered]@{ Operation = $match.Groups['operation'].Value }
    $valid = $true
    foreach ($metric in $metrics) {
        $value = [regex]::Match($line, '(?:\s|,)' + $metric + '=' + $number + '(?:,|\s|$)')
        if (!$value.Success) { $valid = $false; break }
        $record[$metric] = [double]::Parse($value.Groups['value'].Value, [Globalization.CultureInfo]::InvariantCulture)
        if (![double]::IsFinite($record[$metric])) { $valid = $false; break }
    }
    if ($valid) { $rows.Add([pscustomobject]$record) }
} } finally { $reader.Dispose() }
function Percentile($Values, [double]$Fraction) {
    $sorted = @($Values | Sort-Object)
    if (!$sorted.Count) { return $null }
    $sorted[[Math]::Ceiling($sorted.Count * $Fraction) - 1]
}
$summaries = foreach ($operation in $operations) {
    $captured = @($rows | Where-Object Operation -eq $operation)
    $sourceCount = $captured.Count
    if ($UseLastBatch) { $captured = @($captured | Select-Object -Last ($Warmups + $Repetitions)) }
    $measured = @($captured | Select-Object -Skip $Warmups -First $Repetitions)
    $complete = $measured.Count -eq $Repetitions
    $summary = [ordered]@{ Operation = $operation; SourceCaptured = $sourceCount; Captured = $captured.Count; Measured = $measured.Count; Complete = $complete }
    foreach ($metric in $metrics) {
        $summary[$metric] = [ordered]@{
            P50 = if ($complete) { Percentile $measured.$metric .50 } else { $null }
            P95 = if ($complete) { Percentile $measured.$metric .95 } else { $null }
            ObservedP50 = Percentile $measured.$metric .50
        }
    }
    [pscustomobject]$summary
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
$rows | Export-Csv -LiteralPath (Join-Path $target 'allowed-samples.csv') -NoTypeInformation -Encoding utf8
[ordered]@{ Warmups = $Warmups; Repetitions = $Repetitions; Selection = if ($UseLastBatch) { 'last-35-per-operation' } else { 'first-35-per-operation' }; Operations = @($summaries)
    Scope = 'Existing API telemetry, allowed constant operation names and numeric counters only. Not HTTP or rendering latency. Command time ends when EF receives a reader, before complete materialization. Rows have operation-specific meaning. Nested scopes overlap; do not sum parent and child. Missing/incomplete samples do not get certified percentiles. No original log lines, SQL, parameters, tokens or request paths are copied.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $target 'summary.json') -Encoding utf8
$summaries | Select-Object Operation, Captured, Measured, Complete | Format-Table -AutoSize
