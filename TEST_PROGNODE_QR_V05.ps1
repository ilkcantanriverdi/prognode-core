# RC6.4.7 QR pairing acceptance smoke test; does not print passwords, session tokens or QR tickets.
param([string]$CoreBaseUrl = 'http://127.0.0.1:5080', [switch]$RunAuthorizedTest)
$ErrorActionPreference = 'Stop'
function RequestStatus([string]$Url,[string]$Method='GET',[string]$Body='{}',[hashtable]$Headers=@{}) {
    try {
        $result=Invoke-WebRequest -Uri $Url -Method $Method -Headers $Headers -ContentType 'application/json' -Body $(if ($Method -eq 'GET') {$null} else {$Body}) -UseBasicParsing
        return [int]$result.StatusCode
    } catch [System.Net.WebException] {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        throw
    }
}
$health=Invoke-RestMethod "$CoreBaseUrl/api/health"
if($health.coreVersion -ne '0.7.2-rc6.4.7-qr-pairing'){ throw "Wrong Core version: $($health.coreVersion)" }
Write-Host '[PASS] Correct QR Core version' -ForegroundColor Green
$status=RequestStatus "$CoreBaseUrl/api/client/pairing-qr/network-options"
if($status -ne 401) { throw "Without admin session, network-options should return HTTP 401. Got $status" }
Write-Host '[PASS] Unauthenticated local user cannot enumerate QR network options' -ForegroundColor Green
$status=RequestStatus "$CoreBaseUrl/api/client/pairing-qr" 'POST'
if($status -ne 401) { throw "Without admin session, create QR should return HTTP 401. Got $status" }
Write-Host '[PASS] Unauthenticated local user cannot create a QR ticket' -ForegroundColor Green
$status=RequestStatus "$CoreBaseUrl/api/client/pair-qr" 'POST'
if($status -ne 426) { throw "HTTP pair-qr should return HTTPS_REQUIRED (426). Got $status" }
Write-Host '[PASS] HTTP pair-qr refused; HTTPS is required' -ForegroundColor Green
if(-not $RunAuthorizedTest) {
 Write-Host 'Optional: use -RunAuthorizedTest to test an OWNER login, one QR creation, status and cancellation.'
 exit 0
}
$email=Read-Host 'Licensed OWNER email'
$secure=Read-Host 'Licensed OWNER password (not shown)' -AsSecureString
$ptr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {$pass=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)}
finally {[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)}
try {
 $login=Invoke-RestMethod -Uri "$CoreBaseUrl/api/access/login" -Method POST -ContentType 'application/json' -Body (@{email=$email;password=$pass}|ConvertTo-Json -Compress)
} finally {$pass=$null; $secure.Dispose()}
if(-not $login.authenticated -or -not $login.token){throw 'OWNER login failed'}
$headers=@{'X-PROGNODE-Session'=$login.token}
try {
 $addresses=Invoke-RestMethod "$CoreBaseUrl/api/client/pairing-qr/network-options" -Headers $headers
 if(-not $addresses.addresses -or @($addresses.addresses).Count -eq 0){throw 'No active LAN IPv4 interface found. Connect Ethernet/Wi-Fi.'}
 $identity=Invoke-RestMethod "$CoreBaseUrl/api/server/identity"
 if(-not $identity.secureApiPort -or -not $identity.certificateSha256) {
    Write-Warning 'LAN HTTPS is not configured. Run ENABLE_PROGNODE_LAN_HTTPS.ps1 as Administrator then restart Core.'
    exit 0
 }
 $hostAddress=@($addresses.addresses)[0].address
 $created=Invoke-RestMethod "$CoreBaseUrl/api/client/pairing-qr" -Method POST -Headers $headers -ContentType 'application/json' -Body (@{selectedHost=$hostAddress}|ConvertTo-Json -Compress)
 if($created.schemaVersion -ne 1 -or $created.pairingMethod -ne 'QR_TICKET_V1' -or -not $created.qrPayload){throw 'QR create response did not match V0.5 schema'}
 if($created.expiresInSeconds -ne 120){throw 'QR TTL must be 120 seconds'}
 $status=Invoke-RestMethod "$CoreBaseUrl/api/client/pairing-qr/status" -Headers $headers
 if($status.status -ne 'PENDING'){throw "Expected PENDING, got $($status.status)"}
 Write-Host '[PASS] OWNER QR creation + TTL + status; QR secret not printed' -ForegroundColor Green
 $cancel=Invoke-RestMethod "$CoreBaseUrl/api/client/pairing-qr" -Method DELETE -Headers $headers
 $after=Invoke-RestMethod "$CoreBaseUrl/api/client/pairing-qr/status" -Headers $headers
 if($after.status -ne 'REVOKED') {throw "Cancellation failed: $($after.status)"}
 Write-Host '[PASS] QR cancellation revokes pending ticket' -ForegroundColor Green
} finally {
 try{Invoke-RestMethod "$CoreBaseUrl/api/access/logout" -Headers $headers -Method POST -ContentType 'application/json' -Body '{}'|Out-Null}catch{}
}
Write-Host '[PASS] QR smoke test finished. Real Android HTTPS pinning and scanning must be tested by the mobile team.' -ForegroundColor Green
