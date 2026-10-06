param(
    [string]$CoreUrl = "http://127.0.0.1:5080",
    [string]$TagId = ""
)
$ErrorActionPreference = 'Stop'
$expected = '0.7.2-rc6.4.4-trend-refinement'

Write-Host '[Trend Studio] Checking the running Core...' -ForegroundColor Cyan
$health = Invoke-RestMethod "$CoreUrl/api/health"
if ($health.coreVersion -ne $expected) {
    throw "Core version mismatch. Got '$($health.coreVersion)', expected '$expected'. Stop the previous Core service/process before testing."
}
Write-Host "PASS: $($health.coreVersion)" -ForegroundColor Green

$tags = @(Invoke-RestMethod "$CoreUrl/api/tags")
if (-not $TagId -and $tags.Count -gt 0) { $TagId = [string]$tags[0].id }
if (-not $TagId) {
    Write-Host 'NO TAGS: Add a real Modbus/Mock Tag through the normal PROGNODE UI, then rerun this script.' -ForegroundColor Yellow
    exit 0
}

$start = [Uri]::EscapeDataString(([DateTime]::UtcNow.AddHours(-1)).ToString('o'))
$end   = [Uri]::EscapeDataString(([DateTime]::UtcNow).ToString('o'))
$url = "$CoreUrl/api/trend-studio/series?tagIds=$([Uri]::EscapeDataString($TagId))&from=$start&to=$end&maxPoints=1800"
$payload = Invoke-RestMethod $url
if (@($payload.series).Count -ne 1 -or [string]$payload.series[0].tagId -ne $TagId) {
    throw 'Trend Studio did not return the requested Tag series.'
}
if (-not $payload.from -or -not $payload.to) { throw 'Trend response time range missing.' }
Write-Host "PASS: Studio API returned Tag $TagId, $($payload.series[0].sampleCount) historian samples." -ForegroundColor Green

$alarmHistory = @(Invoke-RestMethod "$CoreUrl/api/alarms/history?limit=5")
$batches = @(Invoke-RestMethod "$CoreUrl/api/batches?limit=5")
Write-Host "PASS: Process context API (recent alarms=$($alarmHistory.Count), batches=$($batches.Count))." -ForegroundColor Green
if ($payload.series[0].sampleCount -eq 0) {
    Write-Host 'INFO: No recorded historian samples in the last hour. Configure recording on Historian; never use simulated Core values for production acceptance.' -ForegroundColor Yellow
}
Write-Host 'Open the Trends page in your browser and test zoom, A/B cursor, dual axis, quality gaps and overlays.' -ForegroundColor Cyan
