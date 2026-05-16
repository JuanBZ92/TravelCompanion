param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ResourceGroupName,

    [string]$AppName,

    [string]$ApiUrl,

    [string]$ProjectPath,

    [string]$TerraformDirectory,

    [string]$PublishDirectory,

    [string]$ZipPath,

    [switch]$SkipRestore,

    [switch]$SkipSmokeTest,

    [int]$SmokeTestAttempts = 24,

    [int]$SmokeTestDelaySeconds = 10,

    [string]$SmokeTestPath = "/health",

    [switch]$DisableRunFromPackage,

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
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entryName) | Out-Null
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

$repoRoot = Resolve-RepoRoot

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "src\TravelCompanion.Api\TravelCompanion.Api.csproj"
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

Write-Host "Travel Companion API deploy" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Project: $ProjectPath"
Write-Host "Publish directory: $PublishDirectory"
Write-Host "Zip path: $ZipPath"
Write-Host "Resource group: $ResourceGroupName"
Write-Host "App Service: $AppName"
if (-not [string]::IsNullOrWhiteSpace($ApiUrl)) {
    Write-Host "API URL: $ApiUrl"
}
Write-Host ""

if (-not $DisableRunFromPackage) {
    Invoke-External `
        -Command "az" `
        -Arguments @(
            "webapp",
            "config",
            "appsettings",
            "set",
            "--resource-group", $ResourceGroupName,
            "--name", $AppName,
            "--settings", "WEBSITE_RUN_FROM_PACKAGE=1"
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

$zipArtifact = New-ZipFromDirectory -SourceDirectory $PublishDirectory -DestinationZipPath $ZipPath
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

if ($OpenAzurePortal) {
    $portalUrl = "https://portal.azure.com/#resource/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$AppName/overview"
    Start-Process $portalUrl
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
