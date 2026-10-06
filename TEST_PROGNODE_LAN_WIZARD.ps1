# HF4.1: non-destructive smoke and regression diagnostics.
# Optional: place a local OWNER session token in PROGNODE_TEST_SESSION in this
# PowerShell process; do not save it in a report or pass it on a command line.
param(
 [string]$BaseUrl='http://127.0.0.1:5080',
 [string]$ExpectedPin='',
 [int]$SchannelMinutes=60,
 [switch]$StrictAcceptance
)
$ErrorActionPreference='Stop'
function Assert([bool]$condition,[string]$label){if(-not $condition){throw "[FAIL] $label"} Write-Host "[PASS] $label" -ForegroundColor Green}
function HttpStatus([string]$url,[string]$method='GET',[string]$body=$null){
 try{
  $options=@{Uri=$url;Method=$method;UseBasicParsing=$true;TimeoutSec=8;ErrorAction='Stop'}
  if($body){$options.ContentType='application/json';$options.Body=$body}
  return [int](Invoke-WebRequest @options).StatusCode
 }catch{
  if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode.value__}
  throw
 }
}
$health=Invoke-RestMethod "$BaseUrl/api/health" -TimeoutSec 5
Assert ($health.coreVersion -match 'rc6\.4\.7-hf4\.1(\.2-agent-json-fix|-lan-tls-repair)') 'Correct HF4.1 Core, not stale HF4'
Assert ((HttpStatus "$BaseUrl/api/mobile-access/network-status") -eq 401) 'Anonymous LAN status denied'
Assert ((HttpStatus "$BaseUrl/api/mobile-access/prepare" POST '{"interfaceIndex":1,"selectedHost":"1.1.1.1","scopeType":"SUBNET","confirmPublic":true}') -eq 401) 'Anonymous firewall preparation denied'
Assert ((HttpStatus "$BaseUrl/api/client/pairing-qr" POST '{"selectedHost":"127.0.0.1"}') -eq 401) 'Anonymous QR creation denied'
Assert ((HttpStatus "$BaseUrl/api/mobile-access/phone-probe/fake") -eq 404) 'Expired/fake phone probe rejected'

$session=$env:PROGNODE_TEST_SESSION
if($session){
 $headers=@{'X-PROGNODE-Session'=$session}
 $status=Invoke-RestMethod "$BaseUrl/api/mobile-access/network-status" -Headers $headers -TimeoutSec 8
 $status | Select-Object httpsReady,tlsDiagnosticCode,privateKeyUsable,actualTlsHandshake,firewallRuleHealth,phoneVerified,phoneProbeIp | Format-List
 Assert (-not $status.httpsReady -or ($status.privateKeyUsable -and $status.actualTlsHandshake -and $status.tlsDiagnosticCode -eq 'TLS_OK')) 'HTTPS ready only after signing + pinned localhost handshake'
 if($ExpectedPin){Assert ($status.certificateSha256 -eq $ExpectedPin) 'Original full SHA256 fingerprint preserved'}
 if($StrictAcceptance){
  Assert ($status.httpsReady -and $status.tlsDiagnosticCode -eq 'TLS_OK') 'Core process really uses TLS private key'
  Assert ($status.firewallRuleHealth -eq 'HEALTHY') 'Managed firewall rule healthy'
  Assert ($status.phoneVerified) 'Real approved phone IP completed 5-minute HTTPS probe'
 }
}else{Write-Warning 'Set PROGNODE_TEST_SESSION in this PowerShell process to verify authenticated TLS diagnostic status.'}
if($IsWindows -or $env:OS -eq 'Windows_NT'){
 try{
  $since=(Get-Date).AddMinutes(-[Math]::Abs($SchannelMinutes))
  $schannel=@(Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Schannel';Id=36870;StartTime=$since} -ErrorAction SilentlyContinue)
  if($schannel.Count){
   Write-Warning "$($schannel.Count) Schannel 36870 events in last $SchannelMinutes minutes. Do not declare TLS fixed without a pinned handshake and no NEW events."
  }else{Write-Host 'No recent Schannel 36870 events were found (absence alone is not proof of TLS).' -ForegroundColor Cyan}
 }catch{Write-Warning 'Schannel event log is unavailable; request IT to check Event ID 36870.'}
 $manual=@(Get-NetFirewallRule -DisplayName 'PROGNODE HTTPS Samsung Test' -ErrorAction SilentlyContinue | Where-Object {$_.Enabled -eq 'True'})
 if($manual.Count){
  if($StrictAcceptance){throw '[FAIL] A separate Samsung test firewall rule can mask wizard results; test it in a controlled maintenance window.'}
  Write-Warning 'Separate Samsung test firewall rule still enabled; do not use its phone success as wizard-rule acceptance.'
 }
}
Write-Host 'Schannel permission-denied regression and forbidden device IP must be tested on an ISOLATED Windows test VM (do not revoke live production key ACL).' -ForegroundColor Yellow
Write-Host 'Manual acceptance: compare original pin before/after UAC; restart Core; Public Wi-Fi approved 192.168.1.x phone probe; blocked unapproved IP; actual QR, alarm and occurrence ACK.'
