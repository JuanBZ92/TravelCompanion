param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ResourceGroupName,

    [string]$AppName,

    [string]$ApiUrl,

    [string]$ApiProjectPath,

    [string]$WorkerProjectPath,

    [string]$TerraformDirectory,

    [string]$PublishRoot,

    [string]$ZipPath,

    [string]$WebJobName = "TravelCompanion.Notifications.Worker",

    [switch]$SkipRestore,

    [switch]$SkipSmokeTest,

    [int]$SmokeTestAttempts = 24,

    [int]$SmokeTestDelaySeconds = 10,

    [string]$SmokeTestPath = "/health",

    [switch]$SkipAlwaysOn,

    [switch]$SkipAppSettings,

    [switch]$DisableRunFromPackage,

    [switch]$EnableRunFromPackage,

    [switch]$TrackDeploymentStatus,

    [switch]$OpenAzurePortal
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptRoot = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptRoot "..")).Path
}

function Assert-Command {
    param([string]$Name, [string]$InstallHint)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. $InstallHint"
    }
}

function Invoke-External {
    param(
        [string]$Command,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $originalLocation = Get-Location
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            Set-Location $WorkingDirectory
        }

        Write-Host "$Command $($Arguments -join ' ')" -ForegroundColor DarkGray
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "'$Command' failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Set-Location $originalLocation
    }
}

function Get-TerraformOutput {
    param(
        [string]$TerraformDir,
        [string]$OutputName
    )

    if (-not (Test-Path $TerraformDir)) {
        return $null
    }

    if (-not (Get-Command terraform -ErrorAction SilentlyContinue)) {
        return $null
    }

    try {
        $value = & terraform "-chdir=$TerraformDir" output -raw $OutputName 2>$null
        if ([string]::IsNullOrWhiteSpace($value) -or $value -eq "null") {
            return $null
        }

        return $value.Trim()
    }
    catch {
        return $null
    }
}

function New-ZipFromDirectory {
    param(
        [string]$SourceDirectory,
        [string]$DestinationZipPath
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    if (Test-Path $DestinationZipPath) {
        Remove-Item -LiteralPath $DestinationZipPath -Force
    }

    $destinationParent = Split-Path -Parent $DestinationZipPath
    if (-not (Test-Path $destinationParent)) {
        New-Item -ItemType Directory -Path $destinationParent | Out-Null
    }

    $sourcePath = (Resolve-Path $SourceDirectory).Path
    $zip = [System.IO.Compression.ZipFile]::Open($DestinationZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -Path $sourcePath -Recurse -File | ForEach-Object {
            $entryName = $_.FullName.Substring($sourcePath.Length + 1).Replace("\", "/")
            $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entryName)
            if ($entryName.EndsWith("/run.sh") -or $entryName -eq "run.sh") {
                # Linux WebJobs require run.sh to be executable after ZIP extraction.
                $entry.ExternalAttributes = [BitConverter]::ToInt32([BitConverter]::GetBytes([Convert]::ToUInt32("81ED0000", 16)), 0)
            }
        }
    }
    finally {
        $zip.Dispose()
    }

    return (Get-Item $DestinationZipPath)
}

function Invoke-SmokeTest {
    param(
        [string]$BaseUrl,
        [string]$Path,
        [int]$Attempts,
        [int]$DelaySeconds
    )

    if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
        Write-Warning "Skipping smoke test because API URL is unknown."
        return
    }

    $normalizedUrl = $BaseUrl.TrimEnd("/")
    $normalizedPath = if ($Path.StartsWith("/")) { $Path } else { "/$Path" }
    $healthUrl = "$normalizedUrl$normalizedPath"

    Write-Host "Smoke testing $healthUrl ..." -ForegroundColor Cyan
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 30
            Write-Host "Smoke test passed: HTTP $($response.StatusCode)" -ForegroundColor Green
            return
        }
        catch {
            if ($attempt -eq $Attempts) {
                throw "Smoke test failed after $Attempts attempts. Last error: $($_.Exception.Message)"
            }

            Write-Host "Smoke test attempt $attempt failed. Retrying in $DelaySeconds seconds..." -ForegroundColor Yellow
            Start-Sleep -Seconds $DelaySeconds
        }
    }
}

function Write-Utf8NoBomFile {
    param(
        [string]$Path,
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

$repoRoot = Resolve-RepoRoot

if ([string]::IsNullOrWhiteSpace($ApiProjectPath)) {
    $ApiProjectPath = Join-Path $repoRoot "src\TravelCompanion.Api\TravelCompanion.Api.csproj"
}

if ([string]::IsNullOrWhiteSpace($WorkerProjectPath)) {
    $WorkerProjectPath = Join-Path $repoRoot "src\TravelCompanion.Notifications.Worker\TravelCompanion.Notifications.Worker.csproj"
}

if ([string]::IsNullOrWhiteSpace($TerraformDirectory)) {
    $TerraformDirectory = Join-Path $repoRoot "infra\terraform"
}

if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    $PublishRoot = Join-Path $repoRoot "artifacts\notifications-worker"
}

if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $ZipPath = Join-Path $repoRoot "artifacts\notifications-worker\travelcompanion-api-with-notifications-worker.zip"
}

if (-not (Test-Path $ApiProjectPath)) {
    throw "API project not found at '$ApiProjectPath'."
}

if (-not (Test-Path $WorkerProjectPath)) {
    throw "Worker project not found at '$WorkerProjectPath'."
}

Assert-Command "dotnet" "Install the .NET SDK."
Assert-Command "az" "Install Azure CLI and run 'az login'."

if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) {
    $ResourceGroupName = Get-TerraformOutput -TerraformDir $TerraformDirectory -OutputName "resource_group_name"
}

if ([string]::IsNullOrWhiteSpace($AppName)) {
    $AppName = Get-TerraformOutput -TerraformDir $TerraformDirectory -OutputName "api_app_name"
}

if ([string]::IsNullOrWhiteSpace($ApiUrl)) {
    $ApiUrl = Get-TerraformOutput -TerraformDir $TerraformDirectory -OutputName "api_url"
}

if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) {
    throw "ResourceGroupName was not provided and could not be read from Terraform output."
}

if ([string]::IsNullOrWhiteSpace($AppName)) {
    throw "AppName was not provided and could not be read from Terraform output."
}

if ($EnableRunFromPackage -and $DisableRunFromPackage) {
    throw "Use either EnableRunFromPackage or DisableRunFromPackage, not both."
}

$useRunFromPackage = $EnableRunFromPackage -and (-not $DisableRunFromPackage)

$apiPublishDirectory = Join-Path $PublishRoot "package"
$workerPublishDirectory = Join-Path $PublishRoot "worker-publish"
$webJobDirectory = Join-Path $apiPublishDirectory "App_Data\jobs\continuous\$WebJobName"

Write-Host "Travel Companion notifications worker deploy" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "API project: $ApiProjectPath"
Write-Host "Worker project: $WorkerProjectPath"
Write-Host "WebJob name: $WebJobName"
Write-Host "Package directory: $apiPublishDirectory"
Write-Host "Zip path: $ZipPath"
Write-Host "Resource group: $ResourceGroupName"
Write-Host "App Service: $AppName"
if (-not [string]::IsNullOrWhiteSpace($ApiUrl)) {
    Write-Host "API URL: $ApiUrl"
}
Write-Host ""

if (-not $SkipAppSettings) {
    $appSettings = @(
        "Notifications__Enabled=true",
        "WEBSITE_SKIP_RUNNING_KUDUAGENT=false"
    )
    if ($useRunFromPackage) {
        $appSettings = @("WEBSITE_RUN_FROM_PACKAGE=1") + $appSettings
    }

    Invoke-External `
        -Command "az" `
        -Arguments (@(
            "webapp",
            "config",
            "appsettings",
            "set",
            "--resource-group", $ResourceGroupName,
            "--name", $AppName,
            "--settings"
        ) + $appSettings) `
        -WorkingDirectory $repoRoot

    if (-not $useRunFromPackage) {
        Invoke-External `
            -Command "az" `
            -Arguments @(
                "webapp",
                "config",
                "appsettings",
                "delete",
                "--resource-group", $ResourceGroupName,
                "--name", $AppName,
                "--setting-names", "WEBSITE_RUN_FROM_PACKAGE"
            ) `
            -WorkingDirectory $repoRoot
    }
}

if (-not $SkipAlwaysOn) {
    Invoke-External `
        -Command "az" `
        -Arguments @(
            "webapp",
            "config",
            "set",
            "--resource-group", $ResourceGroupName,
            "--name", $AppName,
            "--always-on", "true"
        ) `
        -WorkingDirectory $repoRoot
}

if (Test-Path $PublishRoot) {
    Remove-Item -LiteralPath $PublishRoot -Recurse -Force
}

$apiPublishArgs = @(
    "publish",
    $ApiProjectPath,
    "-c", $Configuration,
    "-o", $apiPublishDirectory
)

$workerPublishArgs = @(
    "publish",
    $WorkerProjectPath,
    "-c", $Configuration,
    "-o", $workerPublishDirectory
)

if ($SkipRestore) {
    $apiPublishArgs += "--no-restore"
    $workerPublishArgs += "--no-restore"
}

Invoke-External -Command "dotnet" -Arguments $apiPublishArgs -WorkingDirectory $repoRoot
Invoke-External -Command "dotnet" -Arguments $workerPublishArgs -WorkingDirectory $repoRoot

New-Item -ItemType Directory -Path $webJobDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $workerPublishDirectory "*") -Destination $webJobDirectory -Recurse -Force

$runScript = (@(
    '#!/usr/bin/env bash',
    'set -euo pipefail',
    'cd "$(dirname "$0")"',
    'exec dotnet TravelCompanion.Notifications.Worker.dll'
) -join "`n") + "`n"
$settingsJson = "{`n  `"is_singleton`": true,`n  `"stopping_wait_time`": 30`n}`n"
Write-Utf8NoBomFile -Path (Join-Path $webJobDirectory "run.sh") -Content $runScript
Write-Utf8NoBomFile -Path (Join-Path $webJobDirectory "settings.job") -Content $settingsJson

$zipArtifact = New-ZipFromDirectory -SourceDirectory $apiPublishDirectory -DestinationZipPath $ZipPath
Write-Host "Created ZIP: $($zipArtifact.FullName)" -ForegroundColor Green

Invoke-External `
    -Command "az" `
    -Arguments @(
        "webapp",
        "deploy",
        "--resource-group", $ResourceGroupName,
        "--name", $AppName,
        "--src-path", $zipArtifact.FullName,
        "--type", "zip",
        "--clean", "true",
        "--restart", "true",
        "--track-status", ([string]([bool]$TrackDeploymentStatus)).ToLowerInvariant()
    ) `
    -WorkingDirectory $repoRoot

Write-Host "Deploy finished." -ForegroundColor Green

if (-not $SkipSmokeTest) {
    Invoke-SmokeTest -BaseUrl $ApiUrl -Path $SmokeTestPath -Attempts $SmokeTestAttempts -DelaySeconds $SmokeTestDelaySeconds
}

Write-Host ""
Write-Host "WebJob path in package: App_Data/jobs/continuous/$WebJobName" -ForegroundColor Green
Write-Host "Check logs in Azure App Service > WebJobs or Kudu once the app restarts." -ForegroundColor Green

if ($OpenAzurePortal) {
    $subscriptionId = az account show --query id -o tsv
    $portalUrl = "https://portal.azure.com/#resource/subscriptions/$subscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$AppName/webjobs"
    Start-Process $portalUrl
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
