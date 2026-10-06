param([string]$CoreUrl = "http://127.0.0.1:5080")
$ErrorActionPreference = "Stop"
$expected = "0.7.2-rc6.4.4-trend-refinement"
$health = Invoke-RestMethod "$CoreUrl/api/health"
if ($health.coreVersion -ne $expected) {
  Write-Host "FAIL: port 5080 is serving '$($health.coreVersion)', expected '$expected'." -ForegroundColor Red
  Write-Host "An older PROGNODE service/process is probably still running." -ForegroundColor Yellow
  exit 2
}
Write-Host "PASS: correct Core is running: $($health.coreVersion)" -ForegroundColor Green
