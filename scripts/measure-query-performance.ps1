param(
    [string]$BaselineRef = 'cb44e65f9f84d5cd430daaf7e5381004bbccc49e',
    [string]$Output = 'artifacts/query-performance.json'
)
$ErrorActionPreference = 'Stop'
if (-not $env:TRAVELCOMPANION_TEST_POSTGRES) {
    throw 'Set TRAVELCOMPANION_TEST_POSTGRES to a local disposable PostgreSQL database.'
}
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $baselineDirectory = Join-Path $repo 'artifacts/query-baseline'
    New-Item -ItemType Directory -Force $baselineDirectory | Out-Null
    foreach ($name in @('TodayRecommendationService', 'TravelRecommendationPlanningService', 'ProductAnalyticsService', 'ProductAnalyticsRetentionWorker')) {
        $lines = & git show "${BaselineRef}:src/TravelCompanion.Api/Services/$name.cs"
        if ($LASTEXITCODE -ne 0) { throw "Cannot read baseline $name" }
        $source = $lines -join "`n"
        $start = $source.IndexOf("public sealed class $name")
        if ($start -lt 0) { throw "Unexpected baseline shape: $name" }
        $source = $source.Substring(0, $source.IndexOf('namespace ')) + "namespace TravelCompanion.Api.Services;`n`n" + $source.Substring($start)
        $source = $source -replace "\b$name\b", "Baseline$name"
        Set-Content -LiteralPath (Join-Path $baselineDirectory "$name.cs") -Value $source -Encoding utf8
    }
    Set-Content -LiteralPath (Join-Path $baselineDirectory 'revision.txt') -Value $BaselineRef
    & dotnet run --project tools/QueryPerformance/QueryPerformance.csproj --configuration Release -- $Output
    if ($LASTEXITCODE -ne 0) { throw "Performance validation failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }
