# Non-destructive localhost contract check. Does not create firewall rules or touch keys.
param([string]$BaseUrl='http://127.0.0.1:5080')
$ErrorActionPreference='Stop'
$rootUri=[uri]$BaseUrl
if(-not $rootUri.IsLoopback){throw 'The test must target loopback Core only.'}
$expected='0.7.2-rc6.4.7-hf4.1.2-agent-json-fix'
$health=Invoke-RestMethod "$BaseUrl/api/health" -TimeoutSec 5
if($health.coreVersion -ne $expected){throw "Wrong/stale Core: $($health.coreVersion) (expected $expected)."}
Write-Host '[PASS] HF4.1.2 Core health version' -ForegroundColor Green
$url="$BaseUrl/api/mobile-access/agent/pending"
$code=0; $body=''
try{
  $res=Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
  $code=[int]$res.StatusCode
  $body=[string]$res.Content
}catch [System.Net.WebException]{
  $response=$_.Exception.Response
  if($null -eq $response){throw}
  $code=[int]$response.StatusCode
  $stream=$response.GetResponseStream()
  if($stream){$reader=[IO.StreamReader]::new($stream);try{$body=$reader.ReadToEnd()}finally{$reader.Dispose()}}
}
if([string]::IsNullOrWhiteSpace($body)){throw "[FAIL] Pending endpoint returned HTTP $code with EMPTY JSON body."}
try{$json=$body|ConvertFrom-Json -ErrorAction Stop}catch{throw '[FAIL] Pending endpoint returned invalid JSON.'}
if($code -eq 404){
  if($json.code -ne 'NO_PENDING_LAN_REQUEST'){throw "[FAIL] Unexpected no-request code: $($json.code)"}
  Write-Host '[PASS] No active request: JSON HTTP 404 NO_PENDING_LAN_REQUEST' -ForegroundColor Green
}elseif($code -eq 200){
  if(-not $json.requestId -or -not $json.expiresAtUtc){throw '[FAIL] Pending request body is incomplete.'}
  if([DateTimeOffset]::Parse($json.expiresAtUtc) -le [DateTimeOffset]::UtcNow){throw '[FAIL] Core exposes an expired request.'}
  Write-Host '[PASS] Prepared request: populated, unexpired JSON HTTP 200' -ForegroundColor Green
}else{throw "[FAIL] Unexpected pending request HTTP status: $code"}
Write-Host 'Next: exit the OLD Windows tray Agent, launch the NEW HF4.1.2 Agent, and approve the prepared request before its 3-minute expiry.'
