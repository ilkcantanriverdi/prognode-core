param(
  [string]$CoreUrl = "http://127.0.0.1:5080",
  [int]$WaitSeconds = 45
)
$ErrorActionPreference = "Stop"
function Get-Stats {
  Invoke-RestMethod "$CoreUrl/api/historian/stats"
}
$service = Get-Service PROGNODECore -ErrorAction SilentlyContinue
if (!$service) { throw "PROGNODECore service is not installed." }
if ($service.Status -ne "Running") { throw "PROGNODECore service is not running." }
$before = Get-Stats
Write-Host "Before: $($before.totalSamples) historian points" -ForegroundColor Cyan
Write-Host "Close browser/VS Code if you want. Lock Windows if desired. Waiting $WaitSeconds seconds..."
Start-Sleep -Seconds $WaitSeconds
$after = Get-Stats
Write-Host "After : $($after.totalSamples) historian points" -ForegroundColor Cyan
$delta = [int64]$after.totalSamples - [int64]$before.totalSamples
if ($delta -gt 0) {
  Write-Host "PASS: Core recorded $delta new points without the browser." -ForegroundColor Green
  exit 0
}
Write-Host "FAIL/INCONCLUSIVE: no new historian points. Confirm at least one Historian Tag is enabled and its interval is shorter than the wait." -ForegroundColor Yellow
exit 2
