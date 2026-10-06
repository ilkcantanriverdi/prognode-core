$ErrorActionPreference='Stop'
$expected='0.7.2-rc6.4.5-hf2-core-native'
$u='http://127.0.0.1:5080'
$h=Invoke-RestMethod "$u/api/health" -TimeoutSec 5
Write-Host "Core version: $($h.coreVersion)"
if ($h.coreVersion -ne $expected) { throw "OLD CORE ACTIVE: Expected $expected, found $($h.coreVersion). Stop the old Windows Service/process on port 5080." }
$t=Invoke-WebRequest "$u/trend-studio-fullscreen.html?v=rc645hf2" -TimeoutSec 5
if ($t.StatusCode -ne 200 -or $t.Content -notmatch 'trend-studio-fullscreen.js') { throw 'Fullscreen Trend Studio asset missing.' }
$i=Invoke-WebRequest "$u/?build=rc645hf2" -TimeoutSec 5
if ($i.Content -notmatch 'fullscreenTrendFrame') { throw 'Main index.html is not RC6.4.5.' }
Write-Host 'PASS: Correct Core version and full-screen UI assets are being served.' -ForegroundColor Green
