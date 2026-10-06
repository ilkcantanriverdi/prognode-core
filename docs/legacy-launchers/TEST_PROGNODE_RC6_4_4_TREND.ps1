param(
    [string]$CoreUrl = 'http://127.0.0.1:5080',
    [string]$TagId = ''
)
$ErrorActionPreference = 'Stop'
$expected = '0.7.2-rc6.4.4-trend-refinement'

Write-Host '[PROGNODE RC6.4.4] Checking the running Core...' -ForegroundColor Cyan
$health = Invoke-RestMethod "$CoreUrl/api/health"
if ($health.coreVersion -ne $expected) {
    throw "Wrong Core on port 5080: '$($health.coreVersion)' (expected '$expected')."
}
Write-Host "PASS version $expected" -ForegroundColor Green

$tags = @(Invoke-RestMethod "$CoreUrl/api/tags")
$statuses = @(Invoke-RestMethod "$CoreUrl/api/historian/configurations")
$recorded = @($statuses | Where-Object { $_.configuration -and $_.configuration.enabled } | ForEach-Object { [string]$_.configuration.tagId })
Write-Host "Historian recording enabled: $($recorded.Count) Tag(s)" -ForegroundColor Cyan

if ($TagId -and ($TagId -notin $recorded)) {
    throw "Tag $TagId is not enabled in Historian. Configure historian recording first."
}
if (-not $TagId -and $recorded.Count -gt 0) { $TagId = $recorded[0] }
if (-not $TagId) {
    Write-Host 'NO RECORDED TAG: Enable Historian recording for a real Tag in the UI; Trends must remain unavailable until then.' -ForegroundColor Yellow
    if ($tags.Count -gt 0) {
        $candidate = [string]$tags[0].id
        $from = [Uri]::EscapeDataString(([DateTimeOffset]::UtcNow.AddMinutes(-5)).ToString('o'))
        $to = [Uri]::EscapeDataString(([DateTimeOffset]::UtcNow).ToString('o'))
        try {
            Invoke-WebRequest -Uri "$CoreUrl/api/trend-studio/series?tagIds=$candidate&from=$from&to=$to" -UseBasicParsing | Out-Null
            throw 'FAIL: API allowed a Tag with no Historian enrollment.'
        }
        catch [System.Net.WebException] {
            if ([int]$_.Exception.Response.StatusCode -ne 400) { throw }
            Write-Host 'PASS unenrolled Tag rejected by Trend Studio (HTTP 400)' -ForegroundColor Green
        }
    }
    exit 0
}

$durations = @(
    @{Name='5m'; Minutes=5}, @{Name='15m';Minutes=15},
    @{Name='1h';Minutes=60}, @{Name='8h';Minutes=480},
    @{Name='24h';Minutes=1440}, @{Name='7d';Minutes=10080}
)
foreach ($preset in $durations) {
    $endTime = [DateTimeOffset]::UtcNow
    $startTime = $endTime.AddMinutes(-$preset.Minutes)
    $from = [Uri]::EscapeDataString($startTime.ToString('o'))
    $to = [Uri]::EscapeDataString($endTime.ToString('o'))
    $url = "$CoreUrl/api/trend-studio/series?tagIds=$([Uri]::EscapeDataString($TagId))&from=$from&to=$to&maxPoints=1800"
    $payload = Invoke-RestMethod -Uri $url
    if (@($payload.series).Count -ne 1 -or [string]$payload.series[0].tagId -ne $TagId) {
        throw "FAIL $($preset.Name): wrong Tag series returned."
    }
    $actualMinutes = ([DateTimeOffset]$payload.to - [DateTimeOffset]$payload.from).TotalMinutes
    if ([Math]::Abs($actualMinutes - $preset.Minutes) -gt 0.02) {
        throw "FAIL $($preset.Name): server returned $actualMinutes minutes."
    }
    Write-Host "PASS $($preset.Name) window -> $($payload.series[0].sampleCount) sample(s)" -ForegroundColor Green
}
$unenrolled = @($tags | Where-Object { [string]$_.id -notin $recorded })
if ($unenrolled.Count -gt 0) {
    $id = [string]$unenrolled[0].id
    $from = [Uri]::EscapeDataString(([DateTimeOffset]::UtcNow.AddMinutes(-15)).ToString('o'))
    $to = [Uri]::EscapeDataString(([DateTimeOffset]::UtcNow).ToString('o'))
    try {
        Invoke-WebRequest -Uri "$CoreUrl/api/trend-studio/series?tagIds=$id&from=$from&to=$to" -UseBasicParsing | Out-Null
        throw 'FAIL: API allowed an unenrolled Tag.'
    }
    catch [System.Net.WebException] {
        if ([int]$_.Exception.Response.StatusCode -ne 400) { throw }
        Write-Host 'PASS unenrolled Tag rejected (HTTP 400)' -ForegroundColor Green
    }
}
if ($recorded.Count -gt 1) {
    $selected = @($recorded | Select-Object -First 2) -join ','
    $from = [Uri]::EscapeDataString(([DateTimeOffset]::UtcNow.AddHours(-1)).ToString('o'))
    $to = [Uri]::EscapeDataString(([DateTimeOffset]::UtcNow).ToString('o'))
    $pair = Invoke-RestMethod -Uri "$CoreUrl/api/trend-studio/series?tagIds=$selected&from=$from&to=$to"
    if (@($pair.series).Count -ne 2) { throw 'FAIL: Compare API must return two series.' }
    Write-Host 'PASS Compare API: 2 enrolled Tags, 2 distinct series.' -ForegroundColor Green
}
Write-Host 'PASS RC6.4.4 read-only API smoke test. Next: test the rolling LIVE timeline and light theme in the browser.' -ForegroundColor Green
