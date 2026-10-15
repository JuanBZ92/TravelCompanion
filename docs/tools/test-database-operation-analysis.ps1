[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/database-operation-analysis-tests')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
if (!$target.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Repository output only.' }
New-Item -ItemType Directory -Force -Path $target | Out-Null
$lines = [Collections.Generic.List[string]]::new()
1..35 | ForEach-Object {
    $ms = if ($_ -le 5) { 1000 } else { $_ - 5 }
    $lines.Add("Database operation bootstrap: TotalMs=$ms, Commands=7, CommandMs=2.5, ConnectionMs=1E-3, Rows=25, ProcessAgeSeconds=10 privateMarker=synthetic-secret")
}
$lines.Add('Database operation user-secret: TotalMs=999, Commands=7, CommandMs=2.5, ConnectionMs=0, Rows=25, ProcessAgeSeconds=10')
$lines.Add('Database operation bootstrap: TotalMs=-1, Commands=7, CommandMs=2.5, ConnectionMs=0, Rows=25, ProcessAgeSeconds=10')
$lines.Add('Database operation bootstrap: TotalMs=1E999, Commands=7, CommandMs=2.5, ConnectionMs=0, Rows=25, ProcessAgeSeconds=10')
$syntheticLog = Join-Path $target 'synthetic.log'; [IO.File]::WriteAllLines($syntheticLog, $lines)
& (Join-Path $PSScriptRoot 'analyze-database-operations.ps1') -LogPath $syntheticLog -OutputDirectory (Join-Path $OutputDirectory 'analysis') | Out-Null
$report = Get-Content (Join-Path $target 'analysis/summary.json') -Raw | ConvertFrom-Json -Depth 10
$bootstrap = $report.Operations | Where-Object Operation -eq 'bootstrap'
if (!$bootstrap.Complete -or $bootstrap.Captured -ne 35 -or $bootstrap.TotalMs.P50 -ne 15 -or $bootstrap.TotalMs.P95 -ne 29 -or
    $bootstrap.ConnectionMs.P50 -ne .001 -or $bootstrap.Commands.P50 -ne 7) { throw 'Warmup, numeric validation or percentiles incorrect.' }
$absent = $report.Operations | Where-Object Operation -eq 'day_planning'
if ($absent.Complete -or $null -ne $absent.TotalMs.P50) { throw 'Missing operations must stay incomplete/null.' }
$allowed = Get-Content (Join-Path $target 'analysis/allowed-samples.csv') -Raw
if ($allowed.Contains('synthetic-secret') -or $allowed.Contains('user-secret') -or $allowed.Contains('privateMarker')) { throw 'Disallowed metadata leaked.' }
$lines.Add('Database operation bootstrap: TotalMs=99, Commands=7, CommandMs=2.5, ConnectionMs=0, Rows=25, ProcessAgeSeconds=10')
$lines.Add('Database operation bootstrap: TotalMs=101, Commands=7, CommandMs=2.5, ConnectionMs=0, Rows=25, ProcessAgeSeconds=10')
[IO.File]::WriteAllLines($syntheticLog, $lines)
& (Join-Path $PSScriptRoot 'analyze-database-operations.ps1') -LogPath $syntheticLog -UseLastBatch -OutputDirectory (Join-Path $OutputDirectory 'last-batch') | Out-Null
$last = Get-Content (Join-Path $target 'last-batch/summary.json') -Raw | ConvertFrom-Json -Depth 10
$bootstrap = $last.Operations | Where-Object Operation -eq 'bootstrap'
if ($bootstrap.SourceCaptured -ne 37 -or $bootstrap.Captured -ne 35 -or $bootstrap.TotalMs.P50 -ne 17 -or $bootstrap.TotalMs.P95 -ne 99) {
    throw 'Explicit last-batch selection must retain tail outliers and report source counts.'
}
Write-Output 'Database operation analysis tests passed: constants, finite counters, warmups, percentiles and redaction.'
