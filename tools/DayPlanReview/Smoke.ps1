param([string]$BaseUrl = 'http://127.0.0.1:5188')
$ErrorActionPreference = 'Stop'
$uri = [Uri]$BaseUrl
if ($uri.Host -ne '127.0.0.1' -or $uri.Port -ne 5188 -or $uri.Scheme -ne 'http') {
    throw 'This review only supports the synthetic backend at http://127.0.0.1:5188.'
}
function Request([string]$Path, [object]$Body = $null) {
    $arguments = @{ Uri = "$BaseUrl/$Path"; TimeoutSec = 45; Headers = $script:headers }
    if ($null -ne $Body) {
        $arguments.Method = 'Post'
        $arguments.ContentType = 'application/json'
        $arguments.Body = $Body | ConvertTo-Json -Depth 10 -Compress
    }
    Invoke-RestMethod @arguments
}
$script:headers = @{}
$health = Request 'health/ready'
if ($health.status -ne 'ready') { throw 'Local database is not ready.' }
$login = Request 'api/auth/pin-login' @{ pin = '700701' }
if ($login.email -ne 'planner-pass@example.test' -or !$login.tripId) { throw 'Expected the synthetic paid account.' }
$script:headers = @{ Authorization = "Bearer $($login.token)"; 'Accept-Language' = 'es-ES' }
$options = Request 'api/ai/day-plans/options'
$measurements = @()
foreach ($count in @(1, 3, 5, 7)) {
    $payload = @{
        tripId = $options.tripId; expectedRevision = $options.revision; startDate = $options.startsOn
        dayCount = $count; operationId = [Guid]::NewGuid().ToString(); locale = 'es-ES'
        preferences = @{ travelPace = 'balanced'; budget = 'medium'; interests = @('culture', 'food') }
    }
    $proposal = Request 'api/ai/day-plans' $payload
    if ($proposal.days.Count -ne $count) { throw 'The proposed date range changed.' }
    $stops = @($proposal.days | ForEach-Object { $_.stops } | ForEach-Object { $_ })
    if (@($stops.recommendationId | Select-Object -Unique).Count -ne $stops.Count) { throw 'Duplicate places in the proposal.' }
    $measurements += [PSCustomObject]@{ days = $count; ideas = $stops.Count }
}
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($stop in $stops) { [void]$seen.Add($stop.recommendationId) }
$unchangedIds = @($stops | Select-Object -Skip 1 | ForEach-Object { $_.id })
$replacements = 0
foreach ($attempt in 1..5) {
    $previous = $stops[0]
    $replacementRequest = @{
        operationId = $proposal.operationId; tripId = $options.tripId; expectedRevision = $options.revision
        expectedProposalRevision = $proposal.proposalRevision; stopId = $previous.id
        mutationId = [Guid]::NewGuid().ToString(); locale = 'es-ES'; originalRequest = $payload
    }
    $result = Request 'api/ai/day-plans/replace' $replacementRequest
    if (!$result.replaced) { throw 'Expected a different synthetic alternative.' }
    $replacementReplay = Request 'api/ai/day-plans/replace' $replacementRequest
    if (($result | ConvertTo-Json -Depth 20 -Compress) -ne ($replacementReplay | ConvertTo-Json -Depth 20 -Compress)) {
        throw 'Replacement replay changed its result.'
    }
    $proposal = $result.proposal
    $stops = @($proposal.days | ForEach-Object { $_.stops } | ForEach-Object { $_ })
    if ($stops[0].id -eq $previous.id -or !$seen.Add($stops[0].recommendationId)) {
        throw 'Replacement repeated an earlier idea or retained the old identity.'
    }
    if (($unchangedIds -join ',') -ne (($stops | Select-Object -Skip 1 | ForEach-Object { $_.id }) -join ',')) {
        throw 'Replacing one idea changed the other cards.'
    }
    if (@($stops.recommendationId | Select-Object -Unique).Count -ne $stops.Count) {
        throw 'Replacement duplicated another proposed place.'
    }
    $replacements++
}
$canonical = Request 'api/ai/day-plans' $payload
if (($canonical.days | ConvertTo-Json -Depth 20 -Compress) -ne ($proposal.days | ConvertTo-Json -Depth 20 -Compress) -or
    $canonical.proposalRevision -ne $proposal.proposalRevision) { throw 'Generation replay lost the replaced cards.' }
$selection = @($stops | Select-Object -First 2 | ForEach-Object { $_.id })
if ($selection.Count -ne 2) { throw 'Not enough synthetic ideas for the batch check.' }
$application = @{
    operationId = $proposal.operationId; tripId = $options.tripId; expectedRevision = $options.revision
    mutationId = [Guid]::NewGuid().ToString(); selectedStopIds = $selection; locale = 'es-ES'
}
$saved = Request 'api/ai/day-plans/apply' $application
$replay = Request 'api/ai/day-plans/apply' $application
if (!$saved.applied -or $saved.items.Count -ne 2 -or $replay.revision -ne $saved.revision -or
    ($saved.items.id -join ',') -ne ($replay.items.id -join ',')) { throw 'Batch save/replay did not preserve identity.' }
[PSCustomObject]@{ health = $health.status; proposals = $measurements; replacements = $replacements;
    proposalRevision = $proposal.proposalRevision; replacementReplay = 'same result'; saved = $saved.items.Count;
    replay = 'same receipt' } | ConvertTo-Json -Depth 5
# This tool deliberately consumes four paid generations and adds two synthetic plans.
# Tokens stay in memory and never appear in the report. Free-account quota is unchanged.
