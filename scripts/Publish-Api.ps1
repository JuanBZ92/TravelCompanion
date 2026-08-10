param(
    [ValidateSet("Auto", "Azure", "Render")]
    [string]$Provider = "Auto",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ResourceGroupName,

    [string]$AppName,

    [string]$ApiUrl,

    [string]$ProjectPath,

    [string]$WorkerProjectPath,

    [string]$TerraformDirectory,

    [string]$PublishDirectory,

    [string]$ZipPath,

    [switch]$SkipRestore,

    [switch]$SkipSmokeTest,

    [int]$SmokeTestAttempts = 24,

    [int]$SmokeTestDelaySeconds = 10,

    [string]$SmokeTestPath = "/health",

    [switch]$DisableRunFromPackage,

    [switch]$EnableRunFromPackage,

    [switch]$SkipNotificationsWorker,

    [string]$WebJobName = "TravelCompanion.Notifications.Worker",

    [switch]$TrackDeploymentStatus,

    [switch]$OpenAzurePortal,

    [string]$RenderServiceId,

    [string]$RenderApiUrl,

    [string]$RenderBranch,

    [switch]$ClearRenderCache,

    [switch]$SkipRenderGitStatusCheck
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

function Invoke-KuduZipDeploy {
    param(
        [string]$ResourceGroupName,
        [string]$AppName,
        [string]$ZipPath
    )

    Write-Host "Deploying ZIP through Kudu ZipDeploy..." -ForegroundColor Cyan

    $credentialsJson = az webapp deployment list-publishing-credentials `
        --resource-group $ResourceGroupName `
        --name $AppName `
        --output json

    if ($LASTEXITCODE -ne 0) {
        throw "'az webapp deployment list-publishing-credentials' failed with exit code $LASTEXITCODE."
    }

    $credentials = $credentialsJson | ConvertFrom-Json
    $pair = "$($credentials.publishingUserName):$($credentials.publishingPassword)"
    $basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
    $headers = @{ Authorization = "Basic $basic" }
    $deployUri = "https://$AppName.scm.azurewebsites.net/api/zipdeploy?isAsync=true"

    $response = Invoke-WebRequest `
        -Uri $deployUri `
        -Headers $headers `
        -Method Post `
        -InFile $ZipPath `
        -ContentType "application/zip" `
        -UseBasicParsing `
        -TimeoutSec 300

    $statusUri = $response.Headers.Location
    if ([string]::IsNullOrWhiteSpace($statusUri)) {
        Write-Host "ZipDeploy request accepted." -ForegroundColor Green
        return
    }

    do {
        Start-Sleep -Seconds 5
        $status = Invoke-RestMethod -Uri $statusUri -Headers $headers -Method Get -TimeoutSec 60
        if (-not [string]::IsNullOrWhiteSpace($status.progress)) {
            Write-Host $status.progress -ForegroundColor DarkGray
        }
    } while ($status.status -in @(0, 1, 2))

    if ($status.status -ne 4) {
        throw "ZipDeploy failed. Status=$($status.status); Message=$($status.message)"
    }

    Write-Host "ZipDeploy completed successfully." -ForegroundColor Green
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

function Resolve-RenderCli {
    $command = Get-Command "render" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $fallbackPath = Join-Path $env:USERPROFILE ".local\bin\render.exe"
    if (Test-Path $fallbackPath) {
        return $fallbackPath
    }

    throw "Render CLI was not found. Install it or add render.exe to PATH."
}

function Invoke-GitText {
    param(
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $originalLocation = Get-Location
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            Set-Location $WorkingDirectory
        }

        $output = & git @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "'git $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
        }

        return (($output | Out-String).Trim())
    }
    finally {
        Set-Location $originalLocation
    }
}

function Assert-RenderGitState {
    param(
        [string]$RepoRoot,
        [string]$Branch,
        [bool]$SkipCheck
    )

    Assert-Command "git" "Install Git or run this from a Git-enabled shell."

    if ($SkipCheck) {
        return Invoke-GitText -Arguments @("rev-parse", "HEAD") -WorkingDirectory $RepoRoot
    }

    $status = Invoke-GitText -Arguments @("status", "--porcelain") -WorkingDirectory $RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        throw "Render deploy uses GitHub, but the working tree has uncommitted changes. Commit and push them, or pass -SkipRenderGitStatusCheck if you intentionally want to deploy the current remote commit."
    }

    Invoke-External -Command "git" -Arguments @("fetch", "origin", $Branch) -WorkingDirectory $RepoRoot

    $localHead = Invoke-GitText -Arguments @("rev-parse", "HEAD") -WorkingDirectory $RepoRoot
    $remoteHead = Invoke-GitText -Arguments @("rev-parse", "origin/$Branch") -WorkingDirectory $RepoRoot

    if ($localHead -ne $remoteHead) {
        throw "Local HEAD ($($localHead.Substring(0, 7))) does not match origin/$Branch ($($remoteHead.Substring(0, 7))). Push the branch before deploying to Render."
    }

    return $localHead
}

function Invoke-RenderApiDeploy {
    param(
        [string]$RepoRoot,
        [string]$ServiceId,
        [string]$Branch,
        [string]$BaseUrl,
        [bool]$ClearCache,
        [bool]$SkipGitStatusCheck,
        [bool]$SkipSmokeTest,
        [string]$SmokeTestPath,
        [int]$SmokeTestAttempts,
        [int]$SmokeTestDelaySeconds
    )

    $render = Resolve-RenderCli
    if ([string]::IsNullOrWhiteSpace($ServiceId)) {
        throw "RenderServiceId is required for Render deploy."
    }

    if ([string]::IsNullOrWhiteSpace($Branch)) {
        $Branch = Invoke-GitText -Arguments @("branch", "--show-current") -WorkingDirectory $RepoRoot
    }

    if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
        $BaseUrl = "https://travelcompanion-api-57dw.onrender.com"
    }

    Write-Host "Travel Companion API deploy (Render)" -ForegroundColor Cyan
    Write-Host "Service ID: $ServiceId"
    Write-Host "Branch: $Branch"
    Write-Host "API URL: $BaseUrl"
    Write-Host ""

    $commit = Assert-RenderGitState `
        -RepoRoot $RepoRoot `
        -Branch $Branch `
        -SkipCheck $SkipGitStatusCheck

    if (Test-Path (Join-Path $RepoRoot "render.yaml")) {
        Invoke-External `
            -Command $render `
            -Arguments @("blueprints", "validate", "render.yaml", "-o", "text") `
            -WorkingDirectory $RepoRoot
    }

    $deployArgs = @(
        "deploys",
        "create",
        $ServiceId,
        "--commit",
        $commit,
        "--wait",
        "--confirm",
        "-o",
        "text"
    )

    if ($ClearCache) {
        $deployArgs += "--clear-cache"
    }

    try {
        Invoke-External -Command $render -Arguments $deployArgs -WorkingDirectory $RepoRoot
    }
    catch {
        Write-Host ""
        Write-Warning "Render deploy failed. Fetching recent service logs for diagnostics..."
        try {
            Invoke-External `
                -Command $render `
                -Arguments @(
                    "logs",
                    "--resources", $ServiceId,
                    "--limit", "120",
                    "-o", "text"
                ) `
                -WorkingDirectory $RepoRoot
        }
        catch {
            Write-Warning "Could not fetch Render logs automatically: $($_.Exception.Message)"
        }

        throw
    }

    if (-not $SkipSmokeTest) {
        Invoke-SmokeTest -BaseUrl $BaseUrl -Path $SmokeTestPath -Attempts $SmokeTestAttempts -DelaySeconds $SmokeTestDelaySeconds
    }
}

$repoRoot = Resolve-RepoRoot

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "src\TravelCompanion.Api\TravelCompanion.Api.csproj"
}

if ([string]::IsNullOrWhiteSpace($WorkerProjectPath)) {
    $WorkerProjectPath = Join-Path $repoRoot "src\TravelCompanion.Notifications.Worker\TravelCompanion.Notifications.Worker.csproj"
}

if ([string]::IsNullOrWhiteSpace($TerraformDirectory)) {
    $TerraformDirectory = Join-Path $repoRoot "infra\terraform"
}

if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $repoRoot "artifacts\api\publish"
}

if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $ZipPath = Join-Path $repoRoot "artifacts\api\travelcompanion-api.zip"
}

if (-not (Test-Path $ProjectPath)) {
    throw "API project not found at '$ProjectPath'."
}

$resolvedProvider = $Provider
if ($resolvedProvider -eq "Auto") {
    $resolvedProvider = if (Test-Path (Join-Path $repoRoot "render.yaml")) { "Render" } else { "Azure" }
}

if ([string]::IsNullOrWhiteSpace($RenderServiceId)) {
    $RenderServiceId = "srv-d9s7s6p42hec73brl08g"
}

if ([string]::IsNullOrWhiteSpace($RenderApiUrl)) {
    $RenderApiUrl = if (-not [string]::IsNullOrWhiteSpace($ApiUrl)) {
        $ApiUrl
    }
    else {
        "https://travelcompanion-api-57dw.onrender.com"
    }
}

if ([string]::IsNullOrWhiteSpace($RenderBranch)) {
    $RenderBranch = "newapproach"
}

if ($resolvedProvider -eq "Render") {
    Invoke-RenderApiDeploy `
        -RepoRoot $repoRoot `
        -ServiceId $RenderServiceId `
        -Branch $RenderBranch `
        -BaseUrl $RenderApiUrl `
        -ClearCache ([bool]$ClearRenderCache) `
        -SkipGitStatusCheck ([bool]$SkipRenderGitStatusCheck) `
        -SkipSmokeTest ([bool]$SkipSmokeTest) `
        -SmokeTestPath $SmokeTestPath `
        -SmokeTestAttempts $SmokeTestAttempts `
        -SmokeTestDelaySeconds $SmokeTestDelaySeconds

    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
    return
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

$includeNotificationsWorker = -not $SkipNotificationsWorker -and (Test-Path $WorkerProjectPath)
$useRunFromPackage = $EnableRunFromPackage -or ((-not $DisableRunFromPackage) -and (-not $includeNotificationsWorker))

Write-Host "Travel Companion API deploy" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Project: $ProjectPath"
if ($includeNotificationsWorker) {
    Write-Host "Notifications worker: $WorkerProjectPath"
    Write-Host "WebJob name: $WebJobName"
}
Write-Host "Publish directory: $PublishDirectory"
Write-Host "Zip path: $ZipPath"
Write-Host "Resource group: $ResourceGroupName"
Write-Host "App Service: $AppName"
if (-not [string]::IsNullOrWhiteSpace($ApiUrl)) {
    Write-Host "API URL: $ApiUrl"
}
Write-Host ""

$appSettings = @()
if ($useRunFromPackage) {
    $appSettings += "WEBSITE_RUN_FROM_PACKAGE=1"
}

if ($includeNotificationsWorker) {
    $appSettings += "WEBSITE_SKIP_RUNNING_KUDUAGENT=false"
    $appSettings += "Notifications__Enabled=true"
}

if ($appSettings.Count -gt 0) {
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
}

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

if (Test-Path $PublishDirectory) {
    Remove-Item -LiteralPath $PublishDirectory -Recurse -Force
}

$publishArgs = @(
    "publish",
    $ProjectPath,
    "-c", $Configuration,
    "-o", $PublishDirectory
)

if ($SkipRestore) {
    $publishArgs += "--no-restore"
}

Invoke-External -Command "dotnet" -Arguments $publishArgs -WorkingDirectory $repoRoot

if ($includeNotificationsWorker) {
    $workerPublishDirectory = Join-Path (Split-Path -Parent $PublishDirectory) "notifications-worker-publish"
    $webJobDirectory = Join-Path $PublishDirectory "App_Data\jobs\continuous\$WebJobName"

    if (Test-Path $workerPublishDirectory) {
        Remove-Item -LiteralPath $workerPublishDirectory -Recurse -Force
    }

    $workerPublishArgs = @(
        "publish",
        $WorkerProjectPath,
        "-c", $Configuration,
        "-o", $workerPublishDirectory
    )

    if ($SkipRestore) {
        $workerPublishArgs += "--no-restore"
    }

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
}

$zipArtifact = New-ZipFromDirectory -SourceDirectory $PublishDirectory -DestinationZipPath $ZipPath
Write-Host "Created ZIP: $($zipArtifact.FullName)" -ForegroundColor Green

if ($useRunFromPackage) {
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
}
else {
    Invoke-KuduZipDeploy -ResourceGroupName $ResourceGroupName -AppName $AppName -ZipPath $zipArtifact.FullName
}

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

Write-Host "Deploy finished." -ForegroundColor Green

if (-not $SkipSmokeTest) {
    Invoke-SmokeTest -BaseUrl $ApiUrl -Path $SmokeTestPath -Attempts $SmokeTestAttempts -DelaySeconds $SmokeTestDelaySeconds
}

if ($OpenAzurePortal) {
    $portalUrl = "https://portal.azure.com/#resource/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$AppName/overview"
    Start-Process $portalUrl
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
