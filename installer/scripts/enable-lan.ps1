param(
  [Parameter(Mandatory = $true)][string]$InstallRoot
)
# Lets PROGNODE Clients and phones on the local network reach this Core (run by Setup as admin,
# before the service starts). Idempotent: an upgrade keeps the existing certificate, so paired
# devices keep their certificate pin.
#  - HTTPS certificate in LocalMachine\My (reused when one exists) + ProgramData\PROGNODE\config\lan-https.json
#  - Windows Firewall: TCP 5443 (HTTPS API) and UDP 5081 (Client discovery), only for PROGNODE.Core.exe and
#    only from the local subnet. Pairing still needs the code shown in Core and is certificate-pinned.
$ErrorActionPreference = "Stop"
$exe = Join-Path $InstallRoot "Core\PROGNODE.Core.exe"
$configDir = Join-Path $env:ProgramData "PROGNODE\config"
$configPath = Join-Path $configDir "lan-https.json"
$friendlyName = "PROGNODE LAN Core HTTPS"
$group = "PROGNODE LAN Access"

function Get-ValidCert([string]$thumbprint) {
  if (!$thumbprint) { return $null }
  $cert = Get-Item -LiteralPath "Cert:\LocalMachine\My\$thumbprint" -ErrorAction SilentlyContinue
  if ($cert -and $cert.HasPrivateKey -and $cert.NotAfter -gt (Get-Date).AddDays(1)) { return $cert }
  return $null
}

# 1. Certificate: keep the one paired devices already pin.
$certificate = $null
if (Test-Path -LiteralPath $configPath) {
  try { $certificate = Get-ValidCert ((Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).Prognode.LanHttps.Thumbprint) } catch {}
}
if (!$certificate) {
  $existing = @(Get-ChildItem Cert:\LocalMachine\My | Where-Object {
    $_.FriendlyName -eq $friendlyName -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(1) } |
    Sort-Object NotAfter -Descending)
  if ($existing.Count -gt 0) { $certificate = $existing[0] }
}
if (!$certificate) {
  $certificate = New-SelfSignedCertificate -DnsName $env:COMPUTERNAME -FriendlyName $friendlyName `
    -CertStoreLocation "Cert:\LocalMachine\My" -KeyAlgorithm RSA -KeyLength 3072 `
    -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(10) -KeyUsage DigitalSignature, KeyEncipherment
}

# 2. Installation-wide HTTPS settings: admins and LocalSystem write, users only read the public pin.
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($entry in @(@("S-1-5-18", "FullControl"), @("S-1-5-32-544", "FullControl"), @("S-1-5-32-545", "ReadAndExecute"))) {
  $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
    [System.Security.Principal.SecurityIdentifier]::new($entry[0]),
    [System.Security.AccessControl.FileSystemRights]$entry[1],
    [System.Security.AccessControl.InheritanceFlags]"ContainerInherit, ObjectInherit",
    [System.Security.AccessControl.PropagationFlags]::None,
    [System.Security.AccessControl.AccessControlType]::Allow))
}
Set-Acl -LiteralPath $configDir -AclObject $acl
$body = [pscustomobject]@{ Prognode = [pscustomobject]@{ LanHttps = [pscustomobject]@{
  Enabled = $true; Port = 5443; Thumbprint = $certificate.Thumbprint; PfxPath = "" } } }
$stage = Join-Path $configDir ("lan-https." + [guid]::NewGuid().ToString("N") + ".tmp")
$body | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $stage -Encoding UTF8
Move-Item -LiteralPath $stage -Destination $configPath -Force

# 3. Firewall: recreate the two PROGNODE-owned rules so an upgrade fixes drifted ones.
foreach ($name in "PROGNODE-LAN-HTTPS", "PROGNODE-LAN-DISCOVERY") {
  Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}
New-NetFirewallRule -Name "PROGNODE-LAN-HTTPS" -DisplayName "PROGNODE Core LAN (HTTPS)" -Group $group `
  -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5443 -Program $exe `
  -RemoteAddress LocalSubnet -Profile Any -EdgeTraversalPolicy Block | Out-Null
New-NetFirewallRule -Name "PROGNODE-LAN-DISCOVERY" -DisplayName "PROGNODE Core LAN (discovery)" -Group $group `
  -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5081 -Program $exe `
  -RemoteAddress LocalSubnet -Profile Any -EdgeTraversalPolicy Block | Out-Null
