param([string]$CoreUrl = "http://127.0.0.1:5080", [string]$TagId = "")
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/TEST_PROGNODE_RC6_4_TREND.ps1" -CoreUrl $CoreUrl -TagId $TagId
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$alarms = @(Invoke-RestMethod "$CoreUrl/api/alarms/definitions")
$views = @(Invoke-RestMethod "$CoreUrl/api/trends")
$recordings = @(Invoke-RestMethod "$CoreUrl/api/historian/configurations")
Write-Host "Sidebar expected: $($alarms.Count) configured alarms, $($views.Count) saved trends, $($recordings.Count) historian configurations" -ForegroundColor Cyan
if ($TagId) {
  $numeric = @($alarms | Where-Object { [string]$_.tagId -eq $TagId -and [string]$_.condition -ne 'DigitalEquals' -and $_.enabled -ne $false })
  foreach ($def in $numeric) { Write-Host "Alarm threshold: $($def.text) $($def.condition) $($def.threshold)" -ForegroundColor Cyan }
}
Write-Host 'MANUAL ACCEPTANCE: Select an analog Tag on Trends; check threshold-aware Y axis, A/B delta, flag detail and independent visibility switches.' -ForegroundColor Yellow
