[CmdletBinding()]
param(
    [string]$BaselineRef = '51c9962',
    [string]$OutputDirectory = 'artifacts/journal-performance-host',
    [int]$Warmups = 5,
    [int]$Repetitions = 30
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($Warmups -lt 5 -or $Repetitions -lt 30) { throw 'Use at least 5 warmups and 30 repetitions.' }
$measurementOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
if (-not $measurementOutput.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Measurement output must stay inside this repository.'
}
$sourceNames = @('JournalStore.cs', 'JournalStore.FreeEntries.cs', 'JournalStore.DraftPhotos.cs',
    'JournalEntries.cs', 'JournalText.cs', 'ExpenseStore.cs', 'OfflineCacheService.cs')
New-Item -ItemType Directory -Path $measurementOutput -Force | Out-Null
Push-Location $repositoryRoot
try {
    $baselineCommit = (& git rev-parse "${BaselineRef}^{commit}") -join ''
    if ($LASTEXITCODE -ne 0) { throw 'Baseline must resolve to a commit.' }
    $currentHead = (& git rev-parse HEAD) -join ''
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the current Git revision.' }
    $sourceFingerprints = [Collections.Generic.List[object]]::new()
    foreach ($variant in @('baseline', 'current')) {
        $stage = Join-Path $measurementOutput $variant
        $stageSources = Join-Path $stage 'source'
        New-Item -ItemType Directory -Path $stageSources -Force | Out-Null
        foreach ($sourceName in $sourceNames) {
            $repositoryPath = "src/TravelCompanion.Mobile/Services/$sourceName"
            if ($variant -eq 'baseline') {
                $sourceText = (& git show "${BaselineRef}:$repositoryPath") -join "`n"
                if ($LASTEXITCODE -ne 0) { throw "Could not extract $repositoryPath from $BaselineRef" }
            } else { $sourceText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $repositoryPath)) }
            if ($sourceName -eq 'OfflineCacheService.cs') {
                # Same read counter in both variants. Serialization/encryption/write logic remains production code.
                if (-not $sourceText.Contains('File.ReadAllTextAsync(')) { throw 'Cache read instrumentation target changed. Review the harness.' }
                $sourceText = $sourceText.Replace('File.ReadAllTextAsync(', 'BenchmarkIo.ReadAllTextAsync(')
            }
            [IO.File]::WriteAllText((Join-Path $stageSources $sourceName), $sourceText)
            $sourceFingerprints.Add([pscustomobject]@{
                Variant = $variant; File = $sourceName
                Sha256 = (Get-FileHash -LiteralPath (Join-Path $stageSources $sourceName) -Algorithm SHA256).Hash
            })
        }
        $harnessRoot = Join-Path $PSScriptRoot 'JournalPerformance'
        $escapedHarnessRoot = [Security.SecurityElement]::Escape($harnessRoot)
        $escapedRepositoryRoot = [Security.SecurityElement]::Escape($repositoryRoot)
        $projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$escapedHarnessRoot/Program.cs" Link="Program.cs" />
    <Compile Include="$escapedHarnessRoot/PlatformAdapters.cs" Link="PlatformAdapters.cs" />
    <ProjectReference Include="$escapedRepositoryRoot/src/TravelCompanion.Shared/TravelCompanion.Shared.csproj" />
  </ItemGroup>
</Project>
"@
        $projectPath = Join-Path $stage 'JournalPerformance.csproj'
        [IO.File]::WriteAllText($projectPath, $projectXml)
        $logPath = Join-Path $measurementOutput "$variant.log"
        & dotnet run --project $projectPath -c Release --verbosity minimal -- $variant $measurementOutput $Warmups $Repetitions *> $logPath
        $runExit = $LASTEXITCODE
        Get-Content -LiteralPath $logPath -Tail 12
        if ($runExit -ne 0) { throw "$variant measurement failed (exit $runExit). See $logPath" }
    }
    [pscustomobject]@{
        CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        BaselineRef = $BaselineRef; BaselineCommit = $baselineCommit; CurrentHead = $currentHead
        CurrentSource = 'Working tree snapshot, including uncommitted changes'
        SourceFingerprints = $sourceFingerprints
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $measurementOutput 'run-manifest.json')
    $baseline = Get-Content -LiteralPath (Join-Path $measurementOutput 'baseline.json') -Raw | ConvertFrom-Json
    $current = Get-Content -LiteralPath (Join-Path $measurementOutput 'current.json') -Raw | ConvertFrom-Json
    $comparison = foreach ($before in $baseline.scenarios) {
        $after = $current.scenarios | Where-Object Name -eq $before.Name
        [pscustomobject]@{
            Scenario = $before.Name
            BaselineP50Ms = $before.P50Ms; CurrentP50Ms = $after.P50Ms
            BaselineP95Ms = $before.P95Ms; CurrentP95Ms = $after.P95Ms
            BaselineP50AllocatedBytes = $before.P50AllocatedBytes; CurrentP50AllocatedBytes = $after.P50AllocatedBytes
            BaselineP50FileReads = $before.P50FileReads; CurrentP50FileReads = $after.P50FileReads
            BaselineP50BytesRead = $before.P50BytesRead; CurrentP50BytesRead = $after.P50BytesRead
            BaselineP50Requests = $before.P50Requests; CurrentP50Requests = $after.P50Requests
        }
    }
    $comparison | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $measurementOutput 'comparison.json')
    $comparison | Format-Table Scenario, BaselineP50Ms, CurrentP50Ms, BaselineP95Ms, CurrentP95Ms -AutoSize
} finally { Pop-Location }
