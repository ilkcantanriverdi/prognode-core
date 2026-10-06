# PROGNODE LAN Access helper. Installed alongside the trusted Windows Agent.
# Called ONLY after a local tray confirmation + Windows UAC. Never from an HTTP endpoint.
# Production installers MUST Authenticode-sign this helper and install under admin-owned ACLs.
# HF4.1: resolve the ACTUAL Core process identity and grant Read on only its key file.
#requires -RunAsAdministrator
[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidateSet('ENABLE','REPAIR','DISABLE')][string]$Action,
 [Parameter(Mandatory)][ValidateRange(1,2147483647)][int]$InterfaceIndex,
 [Parameter(Mandatory)][ValidatePattern('^\d{1,3}(\.\d{1,3}){3}$')][string]$SelectedHost,
 [Parameter(Mandatory)][ValidateSet('Public','Private')][string]$Profile,
 [Parameter(Mandatory)][ValidateSet('DEVICES','SUBNET')][string]$ScopeType,
 [Parameter(Mandatory)][ValidatePattern('^[0-9.,/]{7,160}$')][string]$RemoteAddresses,
 [Parameter(Mandatory)][string]$ConfigPath,
 [Parameter(Mandatory)][ValidatePattern('^[0-9A-F]{36}$')][string]$RequestId
)
$ErrorActionPreference='Stop'
$group='PROGNODE LAN Access'
$ruleName="PROGNODE-MOBILE-LAN-$InterfaceIndex"
$stateDir=Join-Path $env:ProgramData 'PROGNODE'
$stableConfigDir=Join-Path $stateDir 'config'
$stableConfigPath=Join-Path $stableConfigDir 'lan-https.json'
$statePath=Join-Path $stateDir 'lan-access-state.json'
function Fail([string]$text) { throw "PROGNODE LAN Access: $text" }
function ToUInt([Net.IPAddress]$ip) {
 $b=$ip.GetAddressBytes()
 return (([uint64]$b[0] -shl 24) -bor ([uint64]$b[1] -shl 16) -bor ([uint64]$b[2] -shl 8) -bor [uint64]$b[3])
}
function IsInCidr([Net.IPAddress]$ip,[string]$cidr) {
 $parts=$cidr.Split('/');if($parts.Count -ne 2){return $false}
 $net=$null;$prefix=0
 if(-not [Net.IPAddress]::TryParse($parts[0],[ref]$net) -or -not [int]::TryParse($parts[1],[ref]$prefix) -or $prefix -lt 16 -or $prefix -gt 30){return $false}
 $mask=([uint64]4294967295 -shl (32-$prefix)) -band [uint64]4294967295
 return ((ToUInt $ip) -band $mask) -eq ((ToUInt $net) -band $mask)
}
# Never repair a guessed SID or a guessed private key. The Core process must be
# running locally (the tray request came from its loopback API).
function Resolve-CoreProcessSid {
 $service=Get-CimInstance Win32_Service -Filter "Name='PROGNODECore'" -ErrorAction SilentlyContinue
 $processId=0
 if($service -and $service.State -eq 'Running' -and [int]$service.ProcessId -gt 0) {
  $processId=[int]$service.ProcessId
  $serviceListener=@(Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalAddress -in @('127.0.0.1','::1') } |
    Select-Object -ExpandProperty OwningProcess -Unique)
  if($serviceListener.Count -ne 1 -or [int]$serviceListener[0] -ne $processId) {
   Fail 'CORE_PROCESS_MISMATCH: running PROGNODECore service does not own the local Core API listener.'
  }
 } else {
  $listeners=@(Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalAddress -in @('127.0.0.1','::1') } |
    Select-Object -ExpandProperty OwningProcess -Unique)
  if($listeners.Count -ne 1){ Fail 'CORE_IDENTITY_UNRESOLVED: start exactly one local PROGNODE Core before approving repair.' }
  $processId=[int]$listeners[0]
 }
 $coreProcess=Get-CimInstance Win32_Process -Filter "ProcessId=$processId" -ErrorAction Stop
 if(-not $coreProcess){Fail 'CORE_IDENTITY_UNRESOLVED: Core process exited before setup.'}
 if(-not $service -or $service.State -ne 'Running') {
  if($coreProcess.Name -notmatch '^(dotnet|Prognode.Host)(\.exe)?$' -or
     $coreProcess.CommandLine -notmatch '(?i)Prognode\.Host') {
   Fail 'CORE_IDENTITY_UNRESOLVED: loopback port belongs to an unexpected process.'
  }
 }
 $owner=Invoke-CimMethod -InputObject $coreProcess -MethodName GetOwnerSid -ErrorAction Stop
 if($owner.ReturnValue -ne 0 -or $owner.Sid -notmatch '^S-1-'){Fail 'CORE_IDENTITY_UNRESOLVED: cannot determine actual Windows process SID.'}
 $serviceSid=''
 if($service -and $service.State -eq 'Running'){
  try {
   # Translate the service account to a SID without the extra closing parenthesis
   # that made HF4.1.2 fail during PowerShell parsing, before ANY ACL repair.
   $serviceAccount=[System.Security.Principal.NTAccount]::new('NT SERVICE','PROGNODECore')
   $serviceSid=$serviceAccount.Translate([System.Security.Principal.SecurityIdentifier]).Value
  } catch {
   $serviceSid=''
  }
 }
 return [pscustomobject]@{Pid=$processId; Sid=[string]$owner.Sid; ServiceSid=$serviceSid;
  ServiceRunning=($service -and $service.State -eq 'Running' -and [int]$service.ProcessId -eq $processId)}
}
function Get-PrivateKeyFile([Security.Cryptography.X509Certificates.X509Certificate2]$cert) {
 $key=$null
 try {
  $key=[System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
  if(-not $key){$key=[System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($cert)}
  if(-not $key){Fail 'TLS_KEY_UNSUPPORTED: expected RSA or ECDSA certificate key.'}
  if($key -is [System.Security.Cryptography.RSACng] -or $key -is [System.Security.Cryptography.ECDsaCng]) {
   if(-not $key.Key.IsMachineKey){Fail 'TLS_KEY_NOT_MACHINE: only LocalMachine private keys may be repaired.'}
   $base=Join-Path $env:ProgramData 'Microsoft\Crypto\Keys'
   $name=$key.Key.UniqueName
  } elseif($key -is [System.Security.Cryptography.RSACryptoServiceProvider]) {
   if(-not $key.CspKeyContainerInfo.MachineKeyStore){Fail 'TLS_KEY_NOT_MACHINE: user-store key cannot be repaired.'}
   $base=Join-Path $env:ProgramData 'Microsoft\Crypto\RSA\MachineKeys'
   $name=$key.CspKeyContainerInfo.UniqueKeyContainerName
  } else {Fail 'TLS_KEY_UNSUPPORTED: provider does not expose a supported CNG or CSP key container.'}
  if($name -notmatch '^[a-zA-Z0-9._-]{1,180}$'){Fail 'TLS_KEY_PATH_INVALID: invalid provider container name.'}
  $path=Join-Path $base $name
  if(-not (Test-Path -LiteralPath $path -PathType Leaf)){Fail 'TLS_KEY_FILE_MISSING: machine key file was not found.'}
  return (Get-Item -LiteralPath $path -ErrorAction Stop).FullName
 } finally {if($key){$key.Dispose()}}
}
function Grant-CoreKeyRead([Security.Cryptography.X509Certificates.X509Certificate2]$cert,[string]$sid) {
 $keyFile=Get-PrivateKeyFile $cert
 $identity=[System.Security.Principal.SecurityIdentifier]::new($sid)
 $acl=Get-Acl -LiteralPath $keyFile -ErrorAction Stop
 # Keep the existing ACL intact; add only the exact service/developer SID.
 $rule=[System.Security.AccessControl.FileSystemAccessRule]::new(
   $identity, [Security.AccessControl.FileSystemRights]::Read,
   [Security.AccessControl.InheritanceFlags]::None,
   [Security.AccessControl.PropagationFlags]::None,
   [Security.AccessControl.AccessControlType]::Allow)
 $null=$acl.AddAccessRule($rule)
 Set-Acl -LiteralPath $keyFile -AclObject $acl -ErrorAction Stop
 $verified=Get-Acl -LiteralPath $keyFile -ErrorAction Stop
 $match=@($verified.Access | Where-Object {
   $aceSid='';try{$aceSid=$_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value}catch{}
   $aceSid -eq $sid -and
   $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Read) -eq [Security.AccessControl.FileSystemRights]::Read
 })
 if($match.Count -eq 0){Fail 'TLS_KEY_ACL_FAILED: required narrow Read ACE was not applied.'}
 Write-Host "TLS key ACL repaired for actual Core SID: $sid (Read only)." -ForegroundColor Green
}
function Get-CertPin([Security.Cryptography.X509Certificates.X509Certificate2]$cert){
 return [BitConverter]::ToString(([Security.Cryptography.SHA256]::Create()).ComputeHash($cert.RawData)).Replace('-','')
}
function Test-PinnedLocalTls([string]$expectedPin){
 $connection=$null;$ssl=$null;$reader=$null;$writer=$null
 try {
  $connection=New-Object Net.Sockets.TcpClient
  $async=$connection.BeginConnect('127.0.0.1',5443,$null,$null)
  if(-not $async.AsyncWaitHandle.WaitOne(3500)){return $false}
  $connection.EndConnect($async)
  $callback=[Net.Security.RemoteCertificateValidationCallback]{param($sender,$remote,$chain,$errors)
    if(-not $remote){return $false}
    $x=[System.Security.Cryptography.X509Certificates.X509Certificate2]::new($remote)
    $pin=Get-CertPin $x
    return $pin -eq $expectedPin -and $x.NotBefore -le (Get-Date) -and $x.NotAfter -gt (Get-Date)
  }
  $ssl=New-Object Net.Security.SslStream($connection.GetStream(),$false,$callback)
  $ssl.ReadTimeout=3500; $ssl.WriteTimeout=3500
  $ssl.AuthenticateAsClient('127.0.0.1')
  $writer=[System.IO.StreamWriter]::new($ssl,[Text.Encoding]::ASCII,1024,$true)
  $writer.NewLine="`r`n";$writer.Write("GET /api/server/identity HTTP/1.1`r`nHost: 127.0.0.1`r`nConnection: close`r`n`r`n");$writer.Flush()
  $reader=[System.IO.StreamReader]::new($ssl,[Text.Encoding]::UTF8,$false,1024,$true)
  return ($reader.ReadLine() -match '^HTTP/1\.[01] 200 ')
 } catch {return $false}
 finally {if($reader){$reader.Dispose()};if($writer){$writer.Dispose()};if($ssl){$ssl.Dispose()};if($connection){$connection.Dispose()}}
}
# A signed, admin-owned helper is required in commercial installation; the
# source-tree development copy is intentionally unsigned and NOT production-safe.
$scriptPath=$MyInvocation.MyCommand.Path
$programFiles=@($env:ProgramFiles,${env:ProgramFiles(x86)}) | Where-Object {$_}
foreach($pf in $programFiles){
 if($scriptPath.StartsWith($pf.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) {
  if((Get-AuthenticodeSignature -LiteralPath $scriptPath).Status -ne 'Valid') {
   Fail 'UNSIGNED_PRODUCTION_HELPER: installed helper requires a valid Authenticode signature.'
  }
  $scriptAcl=Get-Acl -LiteralPath $scriptPath
  $writeForUsers=@($scriptAcl.Access | Where-Object {
    $_.AccessControlType -eq 'Allow' -and
    $_.IdentityReference.Value -match '(?i)Everyone|\\Users$|Authenticated Users' -and
    ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Write) -ne 0
  })
  if($writeForUsers.Count -gt 0){Fail 'UNSAFE_HELPER_ACL: standard users must not be able to modify installed helper.'}
 }
}
$adapter=Get-NetAdapter -InterfaceIndex $InterfaceIndex -ErrorAction Stop
if ($adapter.Status -ne 'Up' -or "$($adapter.Name) $($adapter.InterfaceDescription)" -match '(?i)tailscale|vpn' -or
    $adapter.InterfaceDescription -match '(?i)tunnel' -or $adapter.Name -match '[*?\[\]]') { Fail 'Selected adapter is down or is a VPN/Tailscale interface.' }
$profileNow=Get-NetConnectionProfile -InterfaceIndex $InterfaceIndex -ErrorAction Stop
if ($profileNow.NetworkCategory.ToString() -ne $Profile) {Fail 'Windows network profile changed. Return to Core and re-approve the current profile.'}
$ips=@(Get-NetIPAddress -InterfaceIndex $InterfaceIndex -AddressFamily IPv4 -ErrorAction Stop | Where-Object {$_.IPAddress -eq $SelectedHost})
if($ips.Count -ne 1) {Fail 'The chosen local IP is no longer assigned to the selected interface.'}
$hostIp=$null
if(-not [Net.IPAddress]::TryParse($SelectedHost,[ref]$hostIp) -or $hostIp.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork){Fail 'Invalid local IPv4.'}
$prefix=[int]$ips[0].PrefixLength
if($prefix -lt 16 -or $prefix -gt 30) {Fail 'Only explicitly selected ordinary LAN subnets (/16 to /30) are supported.'}
$mask=([uint64]4294967295 -shl (32-$prefix)) -band [uint64]4294967295
$n=(ToUInt $hostIp) -band $mask
$networkIp=[Net.IPAddress]::new([byte[]]@((($n -shr 24) -band 255),(($n -shr 16) -band 255),(($n -shr 8) -band 255),($n -band 255)))
$cidr="$networkIp/$prefix"
$remote=@($RemoteAddresses.Split(',') | Where-Object {$_} | Sort-Object -Unique)
if($remote.Count -eq 0 -or $remote.Count -gt 8){Fail 'At least one and at most eight device addresses are supported.'}
if($ScopeType -eq 'SUBNET') {
 if($remote.Count -ne 1 -or $remote[0] -ne $cidr){Fail 'Approved subnet no longer matches the selected interface.'}
} else {
 foreach($item in $remote){
  if($item.Contains('/')){Fail 'Device allowlist must contain individual IPv4 addresses.'}
  $ip=$null
  if(-not [Net.IPAddress]::TryParse($item,[ref]$ip) -or $ip.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
      -not (IsInCidr $ip $cidr)) {Fail "Device address $item is outside approved subnet."}
 }
}
$managed=@(Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)
foreach($rule in $managed){
 if($rule.Group -ne $group -or $rule.Direction.ToString() -ne 'Inbound') { Fail 'Rule name belongs to another owner; refuse to modify it.' }
}
if($Action -eq 'DISABLE'){
 if($managed.Count -ne 1){Fail 'The selected PROGNODE-managed rule was not found; refusing unrelated firewall changes.'}
 $saved=$null
 if(Test-Path $statePath){$saved=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json}
 if(-not $saved -or [int]$saved.interfaceIndex -ne $InterfaceIndex -or [string]$saved.selectedHost -ne $SelectedHost){Fail 'Stored PROGNODE rule identity does not match.'}
 Remove-NetFirewallRule -Name $ruleName -ErrorAction Stop
 if(Test-Path $statePath){Remove-Item -LiteralPath $statePath -Force}
 Write-Host 'PROGNODE-owned LAN rule removed. Unrelated Windows rules were not changed.' -ForegroundColor Green
 exit 0
}
if(-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf) -or (Split-Path $ConfigPath -Leaf) -ne 'appsettings.json'){
 Fail 'The trusted Core appsettings.json path does not exist.'
}
$json=Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if(-not $json.Prognode -or -not $json.Prognode.LanHttps){Fail 'Invalid Core appsettings structure.'}
# If this is an upgrade with a blank source appsettings, use the original
# installed pin. Never let an unrelated certificate take over an existing Core.
if(Test-Path -LiteralPath $stableConfigPath -PathType Leaf){
 $savedTls=(Get-Content -LiteralPath $stableConfigPath -Raw | ConvertFrom-Json).Prognode.LanHttps
 if(-not $savedTls -or -not $savedTls.Enabled -or -not $savedTls.Thumbprint){Fail 'STABLE_TLS_INVALID: local administrator repair required.'}
 if($json.Prognode.LanHttps.Enabled -and $json.Prognode.LanHttps.Thumbprint -and
    [string]$json.Prognode.LanHttps.Thumbprint -ne [string]$savedTls.Thumbprint){
    Fail 'TLS_PIN_CONFLICT: source config disagrees with installed certificate. No automatic replacement.'
 }
 if($json.Prognode.LanHttps.Enabled -and $json.Prognode.LanHttps.PfxPath){Fail 'TLS_PFX_CONFLICT: active PFX source requires explicit administrator migration.'}
 if(-not $json.Prognode.LanHttps.Thumbprint){$json.Prognode.LanHttps.Thumbprint=[string]$savedTls.Thumbprint}
}

$pfxInUse=-not [string]::IsNullOrWhiteSpace([string]$json.Prognode.LanHttps.PfxPath)
if($pfxInUse -and (-not $json.Prognode.LanHttps.Enabled -or [int]$json.Prognode.LanHttps.Port -ne 5443)){
 Fail 'Existing PFX-based HTTPS configuration needs explicit repair; refusing automatic certificate replacement.'
}
$thumbprint=([string]$json.Prognode.LanHttps.Thumbprint).Replace(' ','')
$certificate=$null
if($pfxInUse){
 Write-Host 'Existing PFX HTTPS configuration preserved without changing any certificate.' -ForegroundColor Cyan
} elseif($thumbprint){
 $certificate=Get-Item -LiteralPath "Cert:\LocalMachine\My\$thumbprint" -ErrorAction SilentlyContinue
 if(-not $certificate -or -not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)){
  Fail 'Configured HTTPS certificate is missing/expired. Explicit certificate recovery is required.'
 }
} else {
 $candidates=@(Get-ChildItem 'Cert:\LocalMachine\My' | Where-Object { $_.FriendlyName -eq 'PROGNODE LAN Core HTTPS' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) })
 if($candidates.Count -gt 1){Fail 'Multiple existing PROGNODE certificates found; cannot guess the trusted pin.'}
 if($candidates.Count -eq 1){$certificate=$candidates[0]}
 else {
  $certificate=New-SelfSignedCertificate -DnsName $env:COMPUTERNAME -FriendlyName 'PROGNODE LAN Core HTTPS' `
      -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 3072 `
      -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(2) -KeyUsage DigitalSignature,KeyEncipherment
 }
}
$coreIdentity=Resolve-CoreProcessSid
$originalPin=if($certificate){Get-CertPin $certificate}else{''}
if($certificate){
 Grant-CoreKeyRead $certificate $coreIdentity.Sid
 if($coreIdentity.ServiceSid -and $coreIdentity.ServiceSid -ne $coreIdentity.Sid){
  Grant-CoreKeyRead $certificate $coreIdentity.ServiceSid
 }
 if((Get-CertPin $certificate) -ne $originalPin){Fail 'TLS_PIN_CHANGED: refusing unexpected certificate change.'}
} elseif($pfxInUse){
 Write-Warning 'PFX certificate is preserved. Core process must already have read access to its existing PFX path; use the in-Core TLS probe to verify.'
}
# Create or repair only the named, app-owned rule. Keep legacy Private rule
# unchanged: disabling it could disconnect a separately configured Ethernet client.
# Show legacy rule migration warning on the Core UI if one is still active.
if($managed.Count -gt 0){ Remove-NetFirewallRule -Name $ruleName -ErrorAction Stop }
try {
 New-NetFirewallRule -Name $ruleName -DisplayName "PROGNODE LAN HTTPS — $($adapter.Name)" `
    -Group $group -Direction Inbound -Action Allow -Enabled True -Protocol TCP -LocalPort 5443 `
    -LocalAddress $SelectedHost -RemoteAddress $remote -InterfaceAlias $adapter.Name `
    -Profile $Profile -EdgeTraversalPolicy Block -ErrorAction Stop | Out-Null
 $rule=Get-NetFirewallRule -Name $ruleName -ErrorAction Stop
 $port=$rule|Get-NetFirewallPortFilter
 $address=$rule|Get-NetFirewallAddressFilter
 $iface=$rule|Get-NetFirewallInterfaceFilter
 if($rule.Enabled.ToString() -ne 'True' -or $rule.Profile.ToString() -ne $Profile -or
    $port.LocalPort.ToString() -ne '5443' -or $address.LocalAddress -ne $SelectedHost -or
    $iface.InterfaceAlias -ne $adapter.Name){Fail 'Firewall rule post-check failed.'}
} catch { throw }
# Do not rotate a working pin. New certificates are stored only in LocalMachine.
$configChanged=$false
if(-not $pfxInUse -and (-not $json.Prognode.LanHttps.Enabled -or $json.Prognode.LanHttps.Thumbprint -ne $certificate.Thumbprint -or
   [int]$json.Prognode.LanHttps.Port -ne 5443)){
 $configBackup=Join-Path (Split-Path $ConfigPath -Parent) ('appsettings.pre-lan-access-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.json')
 Copy-Item -LiteralPath $ConfigPath -Destination $configBackup -ErrorAction Stop
 $json.Prognode.LanHttps.Enabled=$true
 $json.Prognode.LanHttps.Port=5443
 $json.Prognode.LanHttps.Thumbprint=$certificate.Thumbprint
 $json.Prognode.LanHttps.PfxPath=''
 $json | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $ConfigPath -Encoding UTF8 -ErrorAction Stop
 $configChanged=$true
 Write-Warning 'HTTPS configuration saved; the existing Windows service will be restarted if selected.'
}
# Installation-wide HTTPS identity: only local elevated setup changes this file.
# Users may read the public thumbprint, but must never be able to replace it.
if(-not $pfxInUse -and $certificate){
 New-Item -ItemType Directory -Path $stableConfigDir -Force | Out-Null
 $folder=Get-Item -LiteralPath $stableConfigDir
 if(($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){Fail 'Untrusted config directory reparse point.'}
 $directoryAcl=New-Object System.Security.AccessControl.DirectorySecurity
 $directoryAcl.SetAccessRuleProtection($true,$false)
 foreach($entry in @(
   @('S-1-5-18','FullControl'), # LocalSystem
   @('S-1-5-32-544','FullControl'), # Built-in administrators
   @('S-1-5-32-545','ReadAndExecute') # Built-in users: public pin only
 )){
   $sid=[System.Security.Principal.SecurityIdentifier]::new([string]$entry[0])
   $rule=[System.Security.AccessControl.FileSystemAccessRule]::new(
     $sid,
     [System.Security.AccessControl.FileSystemRights]$entry[1],
     [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
     [System.Security.AccessControl.PropagationFlags]::None,
     [System.Security.AccessControl.AccessControlType]::Allow)
   $directoryAcl.AddAccessRule($rule)
 }
 Set-Acl -LiteralPath $stableConfigDir -AclObject $directoryAcl -ErrorAction Stop
 $stableBody=[pscustomobject]@{Prognode=[pscustomobject]@{LanHttps=[pscustomobject]@{
    Enabled=$true;Port=5443;Thumbprint=$certificate.Thumbprint;PfxPath=''
 }}}
 $stage=Join-Path $stableConfigDir ('lan-https.'+[guid]::NewGuid().ToString('N')+'.tmp')
 try{
   $stableBody | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $stage -Encoding UTF8 -ErrorAction Stop
   Move-Item -LiteralPath $stage -Destination $stableConfigPath -Force -ErrorAction Stop
 }finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Force}}
 Write-Host 'Persistent LAN HTTPS configuration saved in ProgramData; updates will reuse this certificate.' -ForegroundColor Green
}
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
[pscustomobject]@{
 interfaceIndex=$InterfaceIndex;interfaceName=$adapter.Name;profile=$Profile;selectedHost=$SelectedHost;
 scopeType=$ScopeType;remoteAddresses=$remote;changedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');ruleName=$ruleName
}|ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding UTF8 -ErrorAction Stop
Write-Host "PROGNODE LAN rule installed: $SelectedHost TCP 5443 / $Profile / $($remote -join ',')" -ForegroundColor Green
if($certificate){
 if($coreIdentity.ServiceRunning){
  $ok=$false
  if(-not $configChanged){$ok=Test-PinnedLocalTls $originalPin}
  if(-not $ok){
   Write-Host 'Restarting the locally identified PROGNODE Core service to retest Schannel under its actual identity.' -ForegroundColor Cyan
   Restart-Service -Name 'PROGNODECore' -ErrorAction Stop
   for($i=0;$i -lt 12;$i++){Start-Sleep -Seconds 1;if(Test-PinnedLocalTls $originalPin){$ok=$true;break}}
  }
  if(-not $ok){Fail 'TLS_HANDSHAKE_FAILED: Core service restarted but pinned localhost TLS + identity is not healthy. Check Schannel 36870; QR stays blocked.'}
  Write-Host 'PASS: pinned TLS + HTTP 200 /api/server/identity under current Core service; original certificate pin preserved.' -ForegroundColor Green
 } else {
  # Only the local elevated helper can write to the admin-owned config
  # directory. The running Core checks BOTH its PID and issue time before
  # gracefully stopping; START_PROGNODE_RUNTIME.ps1 restarts the same Core.
  $restartRequest=Join-Path $stableConfigDir 'lan-restart-request.json'
  $restartStage=Join-Path $stableConfigDir ('lan-restart.'+[guid]::NewGuid().ToString('N')+'.tmp')
  try {
    [pscustomobject]@{processId=[int]$coreIdentity.Pid;requestedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');reason='LAN_HTTPS_RECONFIGURED'} |
      ConvertTo-Json | Set-Content -LiteralPath $restartStage -Encoding UTF8 -ErrorAction Stop
    Move-Item -LiteralPath $restartStage -Destination $restartRequest -Force -ErrorAction Stop
  } finally {if(Test-Path -LiteralPath $restartStage){Remove-Item -LiteralPath $restartStage -Force}}
  Write-Host 'LAN repair completed. Supervised developer Core will restart itself. If started directly from Visual Studio, manually restart that debug process.' -ForegroundColor Green
 }
} else {
 Write-Warning 'PFX mode: use Core Verify to confirm private key usability and pinned localhost TLS before QR pairing.'
}
Write-Host 'Verify real phone HTTPS access from the Core Settings wizard; localhost success alone is insufficient.'
$legacy=@(Get-NetFirewallRule -DisplayName 'PROGNODE LAN HTTPS' -ErrorAction SilentlyContinue | Where-Object {$_.Enabled -eq 'True'})
if($legacy.Count -gt 0){Write-Warning 'An older Private-only PROGNODE firewall rule remains. Review it with IT before disabling; another LAN client may use it.'}
