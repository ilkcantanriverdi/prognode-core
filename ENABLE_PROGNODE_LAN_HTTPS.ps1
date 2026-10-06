#requires -RunAsAdministrator
param([int]$Port = 5443)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $MyInvocation.MyCommand.Path
$config=Join-Path $root 'src\Prognode.Host\appsettings.json'
$stableConfigDir=Join-Path $env:ProgramData 'PROGNODE\config'
$stableConfigPath=Join-Path $stableConfigDir 'lan-https.json'
if(-not (Test-Path $config)) { throw "Run this script in the RC6.4.7 SOURCE project folder." }
$backup=Join-Path $root 'appsettings.before-lan-https.json'
if(-not (Test-Path $backup)) { Copy-Item -LiteralPath $config -Destination $backup -ErrorAction Stop }
$json=Get-Content $config -Raw | ConvertFrom-Json
$thumbprint=([string]$json.Prognode.LanHttps.Thumbprint).Replace(' ', '')
if(Test-Path -LiteralPath $stableConfigPath -PathType Leaf){
 $stored=(Get-Content -LiteralPath $stableConfigPath -Raw | ConvertFrom-Json).Prognode.LanHttps
 if(-not $stored.Enabled -or -not $stored.Thumbprint){throw 'Invalid installed LAN HTTPS identity; repair locally.'}
 if($json.Prognode.LanHttps.Enabled -and $json.Prognode.LanHttps.PfxPath){throw 'TLS_PFX_CONFLICT: active PFX source requires explicit administrator migration.'}
 if($thumbprint -and $thumbprint -ne [string]$stored.Thumbprint){throw 'TLS_PIN_CONFLICT: package certificate differs from installed identity; refusing rotation.'}
 $thumbprint=[string]$stored.Thumbprint
 Write-Host 'Reusing installed PROGNODE HTTPS certificate pin (persistent across releases).' -ForegroundColor Green
}

# A newly extracted source ZIP may have an empty thumbprint even if the old
# installation already used this same PC's certificate. Reuse its pin safely.
if (-not $thumbprint -and $json.Prognode.LanHttps.PfxPath) {
    throw 'The current Core uses a PFX. Do not generate a new certificate; carry over the existing HTTPS settings and PFX securely before starting QR setup.'
}
if (-not $thumbprint) {
    $candidates=@(Get-ChildItem 'Cert:\LocalMachine\My' -ErrorAction Stop | Where-Object {
        $_.FriendlyName -eq 'PROGNODE LAN Core HTTPS' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date)
    })
    if ($candidates.Count -eq 1) {
        $thumbprint=$candidates[0].Thumbprint
        Write-Host 'Found one existing PROGNODE LAN HTTPS certificate. Reusing it to preserve the mobile certificate pin.' -ForegroundColor Green
    } elseif ($candidates.Count -gt 1) {
        throw 'Multiple PROGNODE HTTPS certificates found. Copy the exact original Thumbprint into appsettings.json manually; refusing to guess/rotate the mobile trust pin.'
    }
}
if ($thumbprint) {
    $existing=Get-Item -LiteralPath ("Cert:\LocalMachine\My\"+$thumbprint) -ErrorAction SilentlyContinue
    if (-not $existing -or -not $existing.HasPrivateKey -or $existing.NotAfter -le (Get-Date)) {
        throw 'An HTTPS certificate was configured but is no longer valid/available. Refusing silent rotation. Back up your pairing state and configure explicit certificate recovery before proceeding.'
    }
    $json.Prognode.LanHttps.Enabled=$true
    $json.Prognode.LanHttps.Thumbprint=$existing.Thumbprint
    $json.Prognode.LanHttps.Port=$Port
    $json | ConvertTo-Json -Depth 20 | Set-Content $config -Encoding UTF8
    $certificate=$existing
    Write-Host 'Reusing the existing working LAN HTTPS certificate; paired devices retain their pin.' -ForegroundColor Green
} else {
    # Never issue a new identity silently on a formerly paired Core.
    Write-Warning 'No existing certificate pin was resolved. If this PC already had PROGNODE Mobile pairings, STOP and migrate the original Thumbprint; a newly issued certificate would invalidate their trust.'
    $confirm=Read-Host 'Only on a CLEAN FIRST-TIME Core, type CREATE to generate its first HTTPS certificate'
    if($confirm -cne 'CREATE'){throw 'No certificate created. Import the original certificate identity or run this on a clean first-time Core and explicitly approve CREATE.'}
$hostname=$env:COMPUTERNAME
$certificate=New-SelfSignedCertificate -DnsName $hostname -FriendlyName 'PROGNODE LAN Core HTTPS' `
    -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 3072 `
    -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(2) -KeyUsage DigitalSignature,KeyEncipherment
$json.Prognode.LanHttps.Enabled=$true
$json.Prognode.LanHttps.Port=$Port
$json.Prognode.LanHttps.Thumbprint=$certificate.Thumbprint
$json.Prognode.LanHttps.PfxPath=''
$json | ConvertTo-Json -Depth 20 | Set-Content $config -Encoding UTF8
}
$sha256=[Security.Cryptography.SHA256]::Create()
$sha=([BitConverter]::ToString($sha256.ComputeHash($certificate.RawData))).Replace('-','')
Write-Host "LAN HTTPS enabled; restart Core then pair the mobile client after comparing this fingerprint OUTSIDE discovery:" -ForegroundColor Green
Write-Host "Core HTTPS port: $Port"
Write-Host "Certificate SHA-256: $sha"
Write-Host 'Do not trust an unverified fingerprint received in a UDP broadcast; verify from this local console or an authenticated QR.'
# HTTPS provisioning never opens a blanket Private/Public rule. The local
# Mobile Access wizard creates a constrained rule after explicit NIC/scope consent.
Write-Host 'HTTPS certificate is ready. Open Settings > Mobile Access and authorize a scoped firewall rule via the local Windows Agent.' -ForegroundColor Cyan

# Save installation-wide TLS configuration so future releases do not ask the
# customer to run a version-named startup or provision HTTPS again.
$folder=New-Item -ItemType Directory -Path $stableConfigDir -Force
if(($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Untrusted config directory.'}
$acl=New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
foreach($entry in @(@('S-1-5-18','FullControl'),@('S-1-5-32-544','FullControl'),@('S-1-5-32-545','ReadAndExecute'))){
 $sid=[System.Security.Principal.SecurityIdentifier]::new([string]$entry[0])
 $rule=[System.Security.AccessControl.FileSystemAccessRule]::new(
   $sid,
   [System.Security.AccessControl.FileSystemRights]$entry[1],
   [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
   [System.Security.AccessControl.PropagationFlags]::None,
   [System.Security.AccessControl.AccessControlType]::Allow)
 $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $stableConfigDir -AclObject $acl -ErrorAction Stop
$body=[pscustomobject]@{Prognode=[pscustomobject]@{LanHttps=[pscustomobject]@{Enabled=$true;Port=$Port;Thumbprint=$certificate.Thumbprint;PfxPath=''}}}
$tmp=Join-Path $stableConfigDir ('lan-https.'+[guid]::NewGuid().ToString('N')+'.tmp')
try{$body | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tmp -Encoding UTF8 -ErrorAction Stop
 Move-Item -LiteralPath $tmp -Destination $stableConfigPath -Force -ErrorAction Stop}
finally{if(Test-Path -LiteralPath $tmp){Remove-Item -LiteralPath $tmp -Force}}
Write-Host 'Persistent LAN HTTPS identity saved. Future Core updates reuse the existing certificate.' -ForegroundColor Green
