$ErrorActionPreference='Stop'
$root=$PSScriptRoot
& dotnet run --project (Join-Path $root 'tests\BackupContract\BackupContract.csproj') -c Debug
if($LASTEXITCODE -ne 0){throw 'Backup contract tests failed'}
$base='http://127.0.0.1:5080'
$health=Invoke-RestMethod "$base/api/health" -TimeoutSec 5
if($health.coreVersion -ne '0.7.2-rc6.4.7-hf3-backup'){throw "Wrong Core version: $($health.coreVersion)"}
foreach($url in @('/api/backup/status','/api/backup/audit','/api/backup/download/no-file.pgnbackup')){
  try {Invoke-WebRequest "$base$url" -Method GET -TimeoutSec 5|Out-Null;throw "SECURITY FAIL: $url accessible without session"}
  catch {if([int]$_.Exception.Response.StatusCode -ne 401){throw "Unexpected endpoint response: $url $($_.Exception.Message)"}}
}
$ui=(Invoke-WebRequest "$base/" -TimeoutSec 5).Content
if($ui -notmatch 'backupCenterPanel' -or $ui -notmatch 'qrPairStart' -or $ui -notmatch 'trend-hf3plus.html'){
  throw 'Backup/QR/Trend Studio integration not present'
}
Write-Host '[PASS] Backup cryptography, restore, localhost session guard, QR/HF3 UI' -ForegroundColor Green
