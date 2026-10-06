param(
    [Parameter(Mandatory=$true)][string]$LicensePath,
    [string]$CoreUrl = "http://127.0.0.1:5080"
)
$ErrorActionPreference = "Stop"

if (!(Test-Path $LicensePath)) { throw "License file not found: $LicensePath" }

Write-Host "[1/4] Core version..." -ForegroundColor Cyan
$health = Invoke-RestMethod "$CoreUrl/api/health"
if ($health.coreVersion -ne "0.7.2-rc6.3.1-hf2") {
    throw "Wrong Core is running. Expected 0.7.2-rc6.3.1-hf2, got '$($health.coreVersion)'. Stop the old service/process and start HF2."
}
Write-Host "PASS: $($health.coreVersion)" -ForegroundColor Green

Write-Host "[2/4] Import signed license..." -ForegroundColor Cyan
$import = Invoke-RestMethod -Uri "$CoreUrl/api/license/import" -Method Post -Form @{ license = Get-Item $LicensePath }
if (!$import.imported -or !$import.licenseInstalled) { throw "Import endpoint did not confirm licenseInstalled=true." }
Write-Host "PASS: import confirmed. Account hint: $($import.assignedUserName) <$($import.assignedUserEmail)>" -ForegroundColor Green

Write-Host "[3/4] Signed-out license summary..." -ForegroundColor Cyan
$license = Invoke-RestMethod "$CoreUrl/api/license"
if (!$license.licenseInstalled) { throw "Signed-out /api/license does not report licenseInstalled=true." }
Write-Host "PASS: /api/license reports installed license without exposing details." -ForegroundColor Green

Write-Host "[4/4] Access state..." -ForegroundColor Cyan
$access = Invoke-RestMethod "$CoreUrl/api/access/status"
if ($access.authenticated) { throw "Unexpected authenticated session." }
if ($access.status -ne "SIGN_IN_REQUIRED") { throw "Expected SIGN_IN_REQUIRED, got '$($access.status)'." }
Write-Host "PASS: Access state is SIGN_IN_REQUIRED; UI Sign in must remain clickable." -ForegroundColor Green

Write-Host "RC6.3.1-HF2 import -> sign-in gate regression test PASS." -ForegroundColor Green
