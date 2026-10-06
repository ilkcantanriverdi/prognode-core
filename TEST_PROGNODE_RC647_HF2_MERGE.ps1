param([string]$BaseUrl='http://127.0.0.1:5080')
$ErrorActionPreference='Stop'
$expect='0.7.2-rc6.4.7-hf2-qr-hf3plus'
$health=Invoke-RestMethod -Uri "$BaseUrl/api/health" -TimeoutSec 8
if($health.coreVersion -ne $expect){throw "WRONG CORE version $($health.coreVersion); expected $expect"}
$index=Invoke-WebRequest -Uri "$BaseUrl/" -TimeoutSec 8
if($index.Content -notmatch 'src="/trend-hf3plus.html'){throw 'Native Core does not mount HF3+.'}
if($index.Content -notmatch 'qrPairStart'){throw 'QR Settings missing.'}
$trend=Invoke-WebRequest -Uri "$BaseUrl/trend-hf3plus.html" -TimeoutSec 8
if($trend.Content -notmatch 'trend-hf3plus.js'){throw 'HF3+ frontend HTML missing.'}
$js=Invoke-WebRequest -Uri "$BaseUrl/trend-hf3plus.js" -TimeoutSec 8
if($js.Content -notmatch '/api/trend-studio/series' -or $js.Content -notmatch '/api/historian/configurations'){throw 'Real Historian API integration missing.'}
$qr=Invoke-WebRequest -Uri "$BaseUrl/qr-local.js" -TimeoutSec 8
if($qr.Content -notmatch 'PrognodeQrSvg'){throw 'QR renderer missing.'}
Write-Host "PASS $expect — native Core + HF3+ real Historian + QR UI present" -ForegroundColor Green
Write-Host 'Next: enable an actual Historian Tag and verify live PLC samples in the browser.'

$qrIdentity=Invoke-RestMethod -Uri "$BaseUrl/api/server/identity" -TimeoutSec 8
if (-not $qrIdentity.secureApiPort) {
  Write-Warning 'LAN HTTPS is disabled: run SETUP_PROGNODE_QR.cmd once, restart Core and rerun this test.'
} else { Write-Host "QR LAN HTTPS ready: TCP $($qrIdentity.secureApiPort)" -ForegroundColor Green }
