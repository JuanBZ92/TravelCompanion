param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [string]$Email,

    [string]$Password,

    [string]$Token,

    [string]$DestinationSlug,

    [switch]$IncludeAssistant,

    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"

function Join-Url {
    param([string]$Root, [string]$Path)

    $normalizedRoot = $Root.TrimEnd("/")
    $normalizedPath = if ($Path.StartsWith("/")) { $Path } else { "/$Path" }
    return "$normalizedRoot$normalizedPath"
}

function Invoke-SmokeRequest {
    param(
        [ValidateSet("GET", "POST")]
        [string]$Method = "GET",

        [string]$Path,

        [int[]]$ExpectedStatus = @(200),

        [hashtable]$Headers,

        [object]$Body,

        [string]$Name
    )

    $uri = Join-Url -Root $BaseUrl -Path $Path
    $requestHeaders = @{}
    if ($Headers) {
        foreach ($key in $Headers.Keys) {
            $requestHeaders[$key] = $Headers[$key]
        }
    }

    $request = @{
        Uri             = $uri
        Method          = $Method
        Headers         = $requestHeaders
        UseBasicParsing = $true
        TimeoutSec      = $TimeoutSeconds
    }

    if ($null -ne $Body) {
        $request.ContentType = "application/json"
        $request.Body = $Body | ConvertTo-Json -Depth 8
    }

    try {
        $response = Invoke-WebRequest @request
        $statusCode = [int]$response.StatusCode
        if ($ExpectedStatus -notcontains $statusCode) {
            throw "$Name returned HTTP $statusCode. Expected: $($ExpectedStatus -join ', ')."
        }

        Write-Host "OK  $Name -> HTTP $statusCode" -ForegroundColor Green
        return $response
    }
    catch {
        $response = $_.Exception.Response
        if ($null -ne $response) {
            $statusCode = [int]$response.StatusCode
            if ($ExpectedStatus -contains $statusCode) {
                Write-Host "OK  $Name -> HTTP $statusCode" -ForegroundColor Green
                return $response
            }
        }

        throw "FAIL $Name ($Method $uri): $($_.Exception.Message)"
    }
}

function Get-AuthHeaders {
    param([string]$BearerToken)

    if ([string]::IsNullOrWhiteSpace($BearerToken)) {
        return @{}
    }

    return @{ Authorization = "Bearer $BearerToken" }
}

$BaseUrl = $BaseUrl.TrimEnd("/")
Write-Host "Smoke testing Travel Companion API: $BaseUrl" -ForegroundColor Cyan

Invoke-SmokeRequest -Name "health" -Path "/health" | Out-Null
Invoke-SmokeRequest -Name "destinations" -Path "/api/destinations?page=1&pageSize=1" | Out-Null
Invoke-SmokeRequest -Name "recommendations" -Path "/api/recommendations?page=1&pageSize=1" | Out-Null
Invoke-SmokeRequest -Name "packages" -Path "/api/packages?page=1&pageSize=1" | Out-Null

if ([string]::IsNullOrWhiteSpace($Token) -and
    -not [string]::IsNullOrWhiteSpace($Email) -and
    -not [string]::IsNullOrWhiteSpace($Password)) {
    $login = Invoke-SmokeRequest `
        -Name "auth login" `
        -Method "POST" `
        -Path "/api/auth/login" `
        -Body @{ email = $Email; password = $Password }

    $loginBody = $login.Content | ConvertFrom-Json
    $Token = $loginBody.token
}

if (-not [string]::IsNullOrWhiteSpace($Token)) {
    $headers = Get-AuthHeaders -BearerToken $Token
    $destinationQuery = if ([string]::IsNullOrWhiteSpace($DestinationSlug)) {
        ""
    }
    else {
        "?destinationSlug=$([uri]::EscapeDataString($DestinationSlug))"
    }

    Invoke-SmokeRequest -Name "mobile bootstrap" -Path "/api/mobile/bootstrap$destinationQuery" -Headers $headers | Out-Null
    Invoke-SmokeRequest -Name "mobile discover" -Path "/api/mobile/discover$destinationQuery" -Headers $headers | Out-Null
    Invoke-SmokeRequest -Name "my preference profile" -Path "/api/me/travel-preference-profile" -Headers $headers | Out-Null
    Invoke-SmokeRequest -Name "my schedule" -Path "/api/me/schedule" -Headers $headers -ExpectedStatus @(200, 404) | Out-Null

    if ($IncludeAssistant) {
        Invoke-SmokeRequest `
            -Name "assistant help" `
            -Method "POST" `
            -Path "/api/ai/travel-chat" `
            -Headers $headers `
            -Body @{
                message = "Que puedo pedirte"
                conversationId = $null
                city = $null
                date = $null
                currentLocation = $null
                locale = "es-ES"
            } | Out-Null
    }
}
else {
    Write-Host "Skipping authenticated smoke checks. Pass -Token or -Email/-Password to enable them." -ForegroundColor Yellow
}

Write-Host "Smoke test finished." -ForegroundColor Green
