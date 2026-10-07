[CmdletBinding()]
param(
    [string]$MatrixPath = 'docs/plans/daily-use-clarity.json',
    [string]$EvidencePath = 'artifacts/plan-acceptance-20261007/closure-evidence.json',
    [switch]$ShowFingerprint,
    [switch]$RequireNativeChecks
)

# This is an evidence consistency check, not a substitute for functional review.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Resolve-PlanFile([string]$relativePath) {
    if ([string]::IsNullOrWhiteSpace($relativePath)) { throw 'Referencia de archivo vacía.' }
    $resolved = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativePath))
    $boundary = $repositoryRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Referencia fuera del repositorio: $relativePath"
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Falta evidencia o fuente: $relativePath"
    }
    return $resolved
}

function Get-PlanSnapshot($matrix, [string]$matrixFile) {
    $tracked = @(& git -C $repositoryRoot ls-files --cached --others --exclude-standard -- src tests Directory.Build.props Directory.Build.targets Directory.Packages.props global.json NuGet.Config)
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo obtener el inventario de fuentes.' }
    $referenced = @($matrix.criteria | ForEach-Object { $_.sources })
    $paths = @(@($tracked) + $referenced + @('scripts/check-plan-completion.ps1') |
        Sort-Object -Unique | Where-Object { Test-Path -LiteralPath (Join-Path $repositoryRoot $_) -PathType Leaf })
    $lines = @($paths | ForEach-Object {
        $file = Resolve-PlanFile $_
        "{0}:{1}" -f $_.Replace('\', '/'), (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    })
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    return [ordered]@{
        sourceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        sourceFileCount = $paths.Count
        matrixHash = (Get-FileHash -LiteralPath $matrixFile -Algorithm SHA256).Hash
    }
}

function Assert-RecordedFile($record) {
    $file = Resolve-PlanFile $record.path
    if ($record.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $record.sha256) {
        throw "Hash de evidencia incorrecto: $($record.path)"
    }
    return $file
}

try {
    $matrixFile = Resolve-PlanFile $MatrixPath
    $matrix = Get-Content -LiteralPath $matrixFile -Raw | ConvertFrom-Json
    if ($matrix.schemaVersion -ne 1 -or @($matrix.criteria).Count -eq 0) {
        throw 'Matriz vacía o versión desconocida.'
    }
    $ids = @{}
    foreach ($criterion in $matrix.criteria) {
        if ([string]::IsNullOrWhiteSpace($criterion.id) -or $ids.ContainsKey($criterion.id)) {
            throw "ID vacío o duplicado: $($criterion.id)"
        }
        $ids[$criterion.id] = $true
        if ([string]::IsNullOrWhiteSpace($criterion.acceptance) -or @($criterion.sources).Count -eq 0) {
            throw "Criterio sin aceptación o fuente: $($criterion.id)"
        }
        foreach ($source in $criterion.sources) { $null = Resolve-PlanFile $source }
        if ($criterion.implementation -ne 'done') {
            throw "Código pendiente: $($criterion.id) $($criterion.acceptance)"
        }
        if ($criterion.verification -notin @('automated', 'source_review', 'measured') -or
            [string]::IsNullOrWhiteSpace($criterion.reviewNote)) {
            throw "Falta tipo de verificación o revisión: $($criterion.id)"
        }
        if ($criterion.verification -eq 'automated' -and @($criterion.tests).Count -eq 0) {
            throw "Criterio automático sin prueba: $($criterion.id)"
        }
    }
    $snapshot = Get-PlanSnapshot $matrix $matrixFile
    if ($ShowFingerprint) { $snapshot | ConvertTo-Json; exit 0 }

    $evidence = Get-Content -LiteralPath (Resolve-PlanFile $EvidencePath) -Raw | ConvertFrom-Json
    if ($evidence.schemaVersion -ne 1) { throw 'Versión de evidencia desconocida.' }
    foreach ($recorded in @($evidence.before, $evidence.after)) {
        if ($recorded.sourceHash -ne $snapshot.sourceHash -or
            $recorded.sourceFileCount -ne $snapshot.sourceFileCount -or
            $recorded.matrixHash -ne $snapshot.matrixHash) {
            throw 'Evidencia obsoleta: cambió una fuente o la matriz. Revalidar el snapshot actual.'
        }
    }

    $resultsBySuite = @{}
    $totals = @()
    foreach ($suite in $evidence.suites) {
        if ($resultsBySuite.ContainsKey($suite.id)) { throw "Suite duplicada: $($suite.id)" }
        [xml]$trx = Get-Content -LiteralPath (Assert-RecordedFile $suite) -Raw
        $results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
        $counters = $trx.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($null -eq $counters -or $results.Count -eq 0 -or
            @($results | Where-Object { $_.outcome -ne 'Passed' }).Count -gt 0 -or
            [int]$counters.total -ne $results.Count -or [int]$counters.passed -ne $results.Count) {
            throw "Suite incompleta, fallida u omitida: $($suite.id)"
        }
        $resultsBySuite[$suite.id] = @($results | ForEach-Object { [string]$_.testName })
        $totals += "$($suite.id): $($results.Count) aprobadas"
    }
    foreach ($requiredSuite in $matrix.requiredSuites) {
        if (-not $resultsBySuite.ContainsKey($requiredSuite)) { throw "Falta suite: $requiredSuite" }
    }
    foreach ($criterion in $matrix.criteria) {
        foreach ($test in $criterion.tests) {
            if ([string]::IsNullOrWhiteSpace($test.suite) -or [string]::IsNullOrWhiteSpace($test.prefix)) {
                throw "Prueba sin suite o prefijo: $($criterion.id)"
            }
            if (-not $resultsBySuite.ContainsKey($test.suite) -or
                @($resultsBySuite[$test.suite] | Where-Object {
                    $_.StartsWith($test.prefix, [StringComparison]::Ordinal)
                }).Count -eq 0) {
                throw "Falta prueba aprobada para $($criterion.id): $($test.prefix)"
            }
        }
        foreach ($artifact in $criterion.artifacts) {
            $records = @($evidence.files | Where-Object { $_.path -eq $artifact })
            if ($records.Count -ne 1) { throw "Falta evidencia de $($criterion.id): $artifact" }
            $null = Assert-RecordedFile $records[0]
        }
    }
    foreach ($requiredBuild in $matrix.requiredBuilds) {
        $builds = @($evidence.builds | Where-Object { $_.id -eq $requiredBuild })
        if ($builds.Count -ne 1 -or $builds[0].exitCode -ne 0) { throw "Compilación no aprobada: $requiredBuild" }
        $null = Assert-RecordedFile $builds[0]
    }
    $null = Assert-RecordedFile $evidence.apk
    if ($matrix.PSObject.Properties.Name -contains 'androidArtifact') {
        $metadata = Get-Content -LiteralPath (Assert-RecordedFile $evidence.apkMetadata) -Raw
        $expectedPackage = [regex]::Escape([string]$matrix.androidArtifact.package)
        $expectedVersion = [regex]::Escape([string]$matrix.androidArtifact.versionCode)
        if ($metadata -notmatch "package: name='$expectedPackage' versionCode='$expectedVersion'" ) {
            throw 'La APK no corresponde al package/versión esperados del plan.'
        }
        $signature = Get-Content -LiteralPath (Assert-RecordedFile $evidence.apkSignature) -Raw
        if ($signature -notmatch '(?m)^Verifies\r?$') { throw 'Falta verificación de firma de la APK.' }
    }
    foreach ($check in $matrix.nativeChecks) {
        if ($check.status -notin @('passed', 'pending', 'excluded', 'not_applicable') -or
            [string]::IsNullOrWhiteSpace($check.note)) { throw "Estado nativo inválido: $($check.id)" }
    }
    $pendingNative = @($matrix.nativeChecks | Where-Object { $_.status -eq 'pending' })
    Write-Output "Código revisado: $($matrix.criteria.Count) criterios, sin pendientes conocidos."
    Write-Output ($totals -join '; ')
    Write-Output 'Huella de fuentes, resultados, compilaciones y APK coinciden con la evidencia.'
    if ($pendingNative.Count -gt 0) {
        Write-Output "Validación nativa pendiente: $($pendingNative.Count). No se certifica el cierre visual."
        foreach ($check in $pendingNative) { Write-Output "- $($check.id): $($check.note)" }
        if ($RequireNativeChecks) { exit 2 }
    }
    exit 0
} catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 1
}
