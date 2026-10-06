param(
    [string]$BaseUrl='http://127.0.0.1:5080',
    [string]$SessionToken='',
    [string]$PairedToken=''
)
$ErrorActionPreference='Stop'
$expected='0.7.2-rc6.4.6-mobile-foundation'
$health=Invoke-RestMethod "$BaseUrl/api/health" -TimeoutSec 8
if ($health.coreVersion -ne $expected) { throw "Wrong Core: $($health.coreVersion), expected $expected" }
Write-Host "PASS: Core $expected" -ForegroundColor Green
$server=Invoke-RestMethod "$BaseUrl/api/server/identity" -TimeoutSec 8
Write-Host "Core serverId: $($server.serverId) / $($server.displayName)"
if ([string]::IsNullOrEmpty($SessionToken) -and [string]::IsNullOrEmpty($PairedToken)) {
    Write-Host 'Authenticated mobile endpoint smoke test SKIPPED: pass -SessionToken or -PairedToken. This script never displays tokens.' -ForegroundColor Yellow
    exit 0
}
$headers=@{}
if ($SessionToken) { $headers['X-PROGNODE-Session']=$SessionToken }
if ($PairedToken) { $headers['Authorization']="Bearer $PairedToken" }
$events=Invoke-RestMethod "$BaseUrl/api/mobile/v2/notifications?cursor=0&limit=2" -Headers $headers -TimeoutSec 8
if ($events.schemaVersion -ne 2 -or $null -eq $events.nextCursor) { throw 'Unexpected v2 notification JSON shape.' }
Write-Host "PASS: v2 notification cursor=$($events.nextCursor), events=$(@($events.items).Count)" -ForegroundColor Green
$active=Invoke-RestMethod "$BaseUrl/api/mobile/v2/alarms/active" -Headers $headers -TimeoutSec 8
if ($active.schemaVersion -ne 2) { throw 'Unexpected v2 active alarm JSON shape.' }
Write-Host "PASS: active alarms endpoint, count=$(@($active.items).Count)" -ForegroundColor Green
if ($SessionToken -and !$PairedToken) {
    $n=Invoke-RestMethod "$BaseUrl/api/mobile/v2/notifications/health" -Headers $headers -TimeoutSec 8
    Write-Host "SQLite journal healthy: $($n.storageHealthy), persisted=$($n.persistedEvents), queuedRemote=$($n.queuedRemote)"
}
