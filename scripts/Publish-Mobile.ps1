param(
    [ValidateSet("Android", "iOS", "Both")]
    [string]$Platform = "Android",

    [string]$ApiUrl,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("apk", "aab")]
    [string]$AndroidPackageFormat = "apk",

    [switch]$InstallAndroid,

    [string]$AndroidDevice,

    [string]$MacHost,

    [string]$MacUser,

    [string]$MacRepoPath,

    [string]$IosRuntimeIdentifier = "ios-arm64",

    [switch]$ArchiveIos,

    [string]$CodesignKey,

    [string]$CodesignProvision,

    [switch]$OpenOutput
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptRoot = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptRoot "..")).Path
}

function Resolve-ApiUrl {
    param([string]$ExplicitApiUrl, [string]$RepoRoot)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitApiUrl)) {
        return $ExplicitApiUrl.TrimEnd("/")
    }

    $terraformDir = Join-Path $RepoRoot "infra\terraform"
    if ((Test-Path $terraformDir) -and (Get-Command terraform -ErrorAction SilentlyContinue)) {
        try {
            $terraformOutput = terraform -chdir=$terraformDir output -raw api_url 2>$null
            if (-not [string]::IsNullOrWhiteSpace($terraformOutput)) {
                return $terraformOutput.TrimEnd("/")
            }
        }
        catch {
            Write-Verbose "Could not read terraform output api_url. Falling back to the default dev URL."
        }
    }

    return "https://app-tc-dev-q352ao.azurewebsites.net"
}

function Assert-Command {
    param([string]$Name, [string]$InstallHint)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. $InstallHint"
    }
}

function Test-IsMacOS {
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)
}

function Test-IsWindows {
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
}

function Invoke-DotNet {
    param([string[]]$Arguments)

    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

function Copy-NewestArtifact {
    param(
        [string]$SearchRoot,
        [string[]]$Patterns,
        [string]$DestinationRoot
    )

    $matches = @()
    foreach ($pattern in $Patterns) {
        $matches += Get-ChildItem -Path $SearchRoot -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue
    }

    $artifact = $matches | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $artifact) {
        return $null
    }

    if (-not (Test-Path $DestinationRoot)) {
        New-Item -ItemType Directory -Path $DestinationRoot | Out-Null
    }

    $destination = Join-Path $DestinationRoot $artifact.Name
    Copy-Item -LiteralPath $artifact.FullName -Destination $destination -Force
    return (Get-Item $destination)
}

function Install-AndroidApk {
    param([string]$ApkPath, [string]$Device)

    Assert-Command "adb" "Install Android Platform Tools or open this from a shell where Visual Studio/Android SDK tools are on PATH."

    $devices = & adb devices
    Write-Host ($devices -join [Environment]::NewLine)

    $adbArgs = @()
    if (-not [string]::IsNullOrWhiteSpace($Device)) {
        $adbArgs += @("-s", $Device)
    }
    $adbArgs += @("install", "-r", $ApkPath)

    Write-Host "adb $($adbArgs -join ' ')" -ForegroundColor DarkGray
    & adb @adbArgs
    if ($LASTEXITCODE -ne 0) {
        throw "adb install failed with exit code $LASTEXITCODE."
    }
}

function Publish-Android {
    param(
        [string]$RepoRoot,
        [string]$ProjectPath,
        [string]$ResolvedApiUrl,
        [string]$BuildConfiguration,
        [string]$PackageFormat,
        [bool]$ShouldInstall,
        [string]$Device,
        [string]$ArtifactsRoot
    )

    Write-Host "Publishing Android package..." -ForegroundColor Cyan

    Invoke-DotNet @(
        "publish",
        $ProjectPath,
        "-f", "net10.0-android",
        "-c", $BuildConfiguration,
        "-p:TravelCompanionApiBaseUrl=$ResolvedApiUrl",
        "-p:AndroidPackageFormat=$PackageFormat"
    )

    $extension = if ($PackageFormat -eq "aab") { "*.aab" } else { "*.apk" }
    $artifact = Copy-NewestArtifact `
        -SearchRoot (Join-Path $RepoRoot "src\TravelCompanion.Mobile\bin\$BuildConfiguration\net10.0-android") `
        -Patterns @($extension) `
        -DestinationRoot $ArtifactsRoot

    if ($null -eq $artifact) {
        throw "Android publish finished, but no $PackageFormat artifact was found."
    }

    Write-Host "Android artifact: $($artifact.FullName)" -ForegroundColor Green

    if ($ShouldInstall) {
        if ($PackageFormat -ne "apk") {
            throw "Only APK files can be installed directly with adb. Use -AndroidPackageFormat apk."
        }

        Install-AndroidApk -ApkPath $artifact.FullName -Device $Device
        Write-Host "Android app installed on device." -ForegroundColor Green
    }
}

function Quote-Sh {
    param([string]$Value)
    return "'" + ($Value -replace "'", "'\''") + "'"
}

function Build-IosDotNetArguments {
    param(
        [string]$ProjectPath,
        [string]$ResolvedApiUrl,
        [string]$BuildConfiguration,
        [string]$RuntimeIdentifier,
        [bool]$ShouldArchive,
        [string]$SigningKey,
        [string]$Provision
    )

    $arguments = @(
        "publish",
        $ProjectPath,
        "-f", "net10.0-ios",
        "-c", $BuildConfiguration,
        "-r", $RuntimeIdentifier,
        "-p:TravelCompanionApiBaseUrl=$ResolvedApiUrl"
    )

    if ($ShouldArchive) {
        $arguments += "-p:ArchiveOnBuild=true"
    }

    if (-not [string]::IsNullOrWhiteSpace($SigningKey)) {
        $arguments += "-p:CodesignKey=$SigningKey"
    }

    if (-not [string]::IsNullOrWhiteSpace($Provision)) {
        $arguments += "-p:CodesignProvision=$Provision"
    }

    return $arguments
}

function Publish-IosLocal {
    param(
        [string]$RepoRoot,
        [string]$ProjectPath,
        [string]$ResolvedApiUrl,
        [string]$BuildConfiguration,
        [string]$RuntimeIdentifier,
        [bool]$ShouldArchive,
        [string]$SigningKey,
        [string]$Provision,
        [string]$ArtifactsRoot
    )

    if (-not (Test-IsMacOS)) {
        Write-Warning "iOS publish must run on macOS. Run this script on the Mac, or pass -MacHost/-MacRepoPath to run it over SSH."
        Write-Host ""
        Write-Host "Command to run on the Mac from the repo root:" -ForegroundColor Yellow

        $macCommand = @(
            "pwsh",
            "-File", "scripts/Publish-Mobile.ps1",
            "-Platform", "iOS",
            "-Configuration", $BuildConfiguration,
            "-ApiUrl", $ResolvedApiUrl,
            "-IosRuntimeIdentifier", $RuntimeIdentifier
        )

        if ($ShouldArchive) {
            $macCommand += "-ArchiveIos"
        }

        if (-not [string]::IsNullOrWhiteSpace($SigningKey)) {
            $macCommand += @("-CodesignKey", "`"$SigningKey`"")
        }

        if (-not [string]::IsNullOrWhiteSpace($Provision)) {
            $macCommand += @("-CodesignProvision", "`"$Provision`"")
        }

        Write-Host ($macCommand -join " ")
        return
    }

    Write-Host "Publishing iOS package on this Mac..." -ForegroundColor Cyan

    if ([string]::IsNullOrWhiteSpace($SigningKey) -or [string]::IsNullOrWhiteSpace($Provision)) {
        Write-Warning "No CodesignKey/CodesignProvision was provided. This can work only if signing is already configured on the Mac/project."
    }

    $arguments = Build-IosDotNetArguments `
        -ProjectPath $ProjectPath `
        -ResolvedApiUrl $ResolvedApiUrl `
        -BuildConfiguration $BuildConfiguration `
        -RuntimeIdentifier $RuntimeIdentifier `
        -ShouldArchive $ShouldArchive `
        -SigningKey $SigningKey `
        -Provision $Provision

    Invoke-DotNet $arguments

    $iosOutputRoot = Join-Path $RepoRoot "src/TravelCompanion.Mobile/bin/$BuildConfiguration/net10.0-ios"
    $artifact = Copy-NewestArtifact -SearchRoot $iosOutputRoot -Patterns @("*.ipa", "*.app") -DestinationRoot $ArtifactsRoot
    if ($null -ne $artifact) {
        Write-Host "iOS artifact: $($artifact.FullName)" -ForegroundColor Green
    }
    else {
        Write-Host "iOS publish finished. Check output under: $iosOutputRoot" -ForegroundColor Yellow
    }
}

function Publish-IosRemote {
    param(
        [string]$HostName,
        [string]$UserName,
        [string]$RemoteRepoPath,
        [string]$ResolvedApiUrl,
        [string]$BuildConfiguration,
        [string]$RuntimeIdentifier,
        [bool]$ShouldArchive,
        [string]$SigningKey,
        [string]$Provision
    )

    Assert-Command "ssh" "Install OpenSSH Client or run the iOS publish script directly on the Mac."

    if ([string]::IsNullOrWhiteSpace($RemoteRepoPath)) {
        throw "MacRepoPath is required when publishing iOS over SSH."
    }

    $target = if ([string]::IsNullOrWhiteSpace($UserName)) { $HostName } else { "$UserName@$HostName" }
    $remoteProjectPath = "src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj"

    $arguments = Build-IosDotNetArguments `
        -ProjectPath $remoteProjectPath `
        -ResolvedApiUrl $ResolvedApiUrl `
        -BuildConfiguration $BuildConfiguration `
        -RuntimeIdentifier $RuntimeIdentifier `
        -ShouldArchive $ShouldArchive `
        -SigningKey $SigningKey `
        -Provision $Provision

    $dotnetCommand = "dotnet " + (($arguments | ForEach-Object { Quote-Sh $_ }) -join " ")
    $remoteCommand = "cd $(Quote-Sh $RemoteRepoPath) && $dotnetCommand"

    Write-Host "Running iOS publish on $target..." -ForegroundColor Cyan
    Write-Host "ssh $target $remoteCommand" -ForegroundColor DarkGray
    & ssh $target $remoteCommand
    if ($LASTEXITCODE -ne 0) {
        throw "Remote iOS publish failed with exit code $LASTEXITCODE."
    }

    Write-Host "iOS publish finished on the Mac. Check: $RemoteRepoPath/src/TravelCompanion.Mobile/bin/$BuildConfiguration/net10.0-ios" -ForegroundColor Green
}

$repoRoot = Resolve-RepoRoot
$projectPath = Join-Path $repoRoot "src\TravelCompanion.Mobile\TravelCompanion.Mobile.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts\mobile"
$resolvedApiUrl = Resolve-ApiUrl -ExplicitApiUrl $ApiUrl -RepoRoot $repoRoot

if (-not (Test-Path $projectPath)) {
    throw "Mobile project not found at $projectPath."
}

Assert-Command "dotnet" "Install .NET SDK 10 and MAUI workloads."

Write-Host "Travel Companion mobile publish" -ForegroundColor Cyan
Write-Host "Platform: $Platform"
Write-Host "Configuration: $Configuration"
Write-Host "API URL: $resolvedApiUrl"
Write-Host ""

if ($Platform -eq "Android" -or $Platform -eq "Both") {
    Publish-Android `
        -RepoRoot $repoRoot `
        -ProjectPath $projectPath `
        -ResolvedApiUrl $resolvedApiUrl `
        -BuildConfiguration $Configuration `
        -PackageFormat $AndroidPackageFormat `
        -ShouldInstall ([bool]$InstallAndroid) `
        -Device $AndroidDevice `
        -ArtifactsRoot $artifactsRoot
}

if ($Platform -eq "iOS" -or $Platform -eq "Both") {
    if (-not [string]::IsNullOrWhiteSpace($MacHost)) {
        Publish-IosRemote `
            -HostName $MacHost `
            -UserName $MacUser `
            -RemoteRepoPath $MacRepoPath `
            -ResolvedApiUrl $resolvedApiUrl `
            -BuildConfiguration $Configuration `
            -RuntimeIdentifier $IosRuntimeIdentifier `
            -ShouldArchive ([bool]$ArchiveIos) `
            -SigningKey $CodesignKey `
            -Provision $CodesignProvision
    }
    else {
        Publish-IosLocal `
            -RepoRoot $repoRoot `
            -ProjectPath $projectPath `
            -ResolvedApiUrl $resolvedApiUrl `
            -BuildConfiguration $Configuration `
            -RuntimeIdentifier $IosRuntimeIdentifier `
            -ShouldArchive ([bool]$ArchiveIos) `
            -SigningKey $CodesignKey `
            -Provision $CodesignProvision `
            -ArtifactsRoot $artifactsRoot
    }
}

if ($OpenOutput -and (Test-Path $artifactsRoot) -and (Test-IsWindows)) {
    Invoke-Item $artifactsRoot
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
