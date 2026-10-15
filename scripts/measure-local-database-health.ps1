[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Database,
    [string]$HostName = '127.0.0.1',
    [int]$Port = 55439,
    [string]$User = 'postgres',
    [string]$Psql = 'psql',
    [string]$PreviousSnapshot,
    [string]$OutputDirectory = 'artifacts/local-database-health'
)
$ErrorActionPreference = 'Stop'
if ($HostName -notin @('127.0.0.1', 'localhost', '::1')) { throw 'Local synthetic PostgreSQL only; production and tunnels are excluded.' }
if ($Database -notmatch '^tc_[A-Za-z0-9_]+$') { throw 'Use a dedicated tc_ synthetic database.' }
if ($Port -lt 1 -or $Port -gt 65535) { throw 'Invalid local PostgreSQL port.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
if (-not $destination.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Diagnostic output must stay inside this repository.'
}
# Deliberately reads catalog/statistics only: no table content, SQL text, parameters or connection strings.
# Statistics are estimates since the last analyze/reset; no full table counts or EXPLAIN load here.
$sql = @'
BEGIN READ ONLY;
SET LOCAL statement_timeout = '5s';
SELECT json_build_object(
  'capturedAtUtc', to_char(clock_timestamp() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
  'database', current_database(),
  'serverVersion', current_setting('server_version'),
  'databaseBytes', pg_database_size(current_database()),
  'maxConnections', current_setting('max_connections')::int,
  'databaseConnections', (SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()),
  'serverConnections', (SELECT count(*) FROM pg_stat_activity WHERE backend_type = 'client backend'),
  'waitingConnections', (SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'),
  'idleInTransaction', (SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND state = 'idle in transaction'),
  'statistics', (SELECT json_build_object('commits', xact_commit, 'rollbacks', xact_rollback,
     'blocksRead', blks_read, 'blocksHit', blks_hit, 'tempBytes', temp_bytes, 'deadlocks', deadlocks,
     'resetAt', stats_reset) FROM pg_stat_database WHERE datname = current_database()),
  'tables', (SELECT coalesce(json_agg(json_build_object('schema', schemaname, 'table', relname,
     'estimatedLiveRows', n_live_tup, 'estimatedDeadRows', n_dead_tup,
     'tableBytes', pg_table_size(relid), 'indexBytes', pg_indexes_size(relid),
     'totalBytes', pg_total_relation_size(relid), 'sequentialScans', seq_scan, 'indexScans', idx_scan,
     'lastAnalyze', last_analyze, 'lastAutoanalyze', last_autoanalyze)
     ORDER BY pg_total_relation_size(relid) DESC), '[]'::json)
     FROM pg_stat_user_tables WHERE schemaname = 'public')
);
ROLLBACK;
'@
$raw = & $Psql --no-psqlrc --no-password --quiet --tuples-only --no-align --host $HostName --port $Port --username $User --dbname $Database --set ON_ERROR_STOP=1 --command $sql
if ($LASTEXITCODE -ne 0) { throw "Local metadata query failed (exit $LASTEXITCODE)." }
$snapshot = ($raw -join "`n") | ConvertFrom-Json -Depth 10
$growth = @()
if ($PreviousSnapshot) {
    $old = Get-Content -LiteralPath $PreviousSnapshot -Raw | ConvertFrom-Json -Depth 10
    if ($old.database -cne $snapshot.database) { throw 'Growth snapshots must describe the same synthetic database.' }
    $growth = @($snapshot.tables | ForEach-Object {
        $table = $_
        $prior = $old.tables | Where-Object { $_.schema -ceq $table.schema -and $_.table -ceq $table.table }
        if ($prior) {
            [pscustomobject]@{ Schema = $table.schema; Table = $table.table
                EstimatedLiveRowsDelta = $table.estimatedLiveRows - $prior.estimatedLiveRows
                TotalBytesDelta = $table.totalBytes - $prior.totalBytes; IndexBytesDelta = $table.indexBytes - $prior.indexBytes }
        }
    })
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'snapshot.json') -Encoding utf8
[ordered]@{ BaselineCapturedAtUtc = if ($PreviousSnapshot) { $old.capturedAtUtc } else { $null }
    CurrentCapturedAtUtc = $snapshot.capturedAtUtc; Growth = $growth
    Interpretation = 'Read-only local synthetic metadata. Live/dead row counts and scan counters are statistics, not exact counts or proof of a bottleneck. Connections include this diagnostic session. Byte deltas do not establish a growth rate without a representative elapsed interval. SQL telemetry and EXPLAIN remain available in the existing query benchmark; do not add indexes from a single scan counter.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'growth.json') -Encoding utf8
$snapshot | Select-Object capturedAtUtc, databaseBytes, databaseConnections, maxConnections, waitingConnections, idleInTransaction | Format-List
