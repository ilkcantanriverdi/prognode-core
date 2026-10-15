param(
  [Parameter(Mandatory = $true)][string]$InstallRoot,
  [Parameter(Mandatory = $true)][string]$DataRoot
)
# Registers (or re-registers) the PROGNODE Monitor Windows service (service name PROGNODECore) and starts it.
$ErrorActionPreference = "Stop"
$name = "PROGNODECore"
$exe = Join-Path $InstallRoot "Core\PROGNODE.Core.exe"
if (!(Test-Path $exe)) { throw "PROGNODE Monitor executable not found: $exe" }
New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null

# The service runs as LocalSystem: only SYSTEM and Administrators may write or read its data (database,
# device passwords, downloaded updates). ProgramData would otherwise let every local user create files here.
$root = Split-Path $DataRoot -Parent
if ((Split-Path $root -Leaf) -ne "PROGNODE") { $root = $DataRoot }
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in @("S-1-5-18", "S-1-5-32-544")) {
  $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
    [System.Security.Principal.SecurityIdentifier]::new($sid), "FullControl",
    "ContainerInherit, ObjectInherit", "None", "Allow"))
}
Set-Acl -LiteralPath $root -AclObject $acl
# Anything a user created here earlier stays theirs otherwise: take ownership and drop its own rules.
# config keeps its deliberate read-only rule for users (enable-lan.ps1).
& icacls.exe $root /setowner "*S-1-5-32-544" /T /C /Q | Out-Null
Get-ChildItem -LiteralPath $root -Force | Where-Object { $_.Name -ne "config" } |
  ForEach-Object { & icacls.exe $_.FullName /reset /T /C /Q | Out-Null }

$existing = Get-Service -Name $name -ErrorAction SilentlyContinue
if ($existing) {
  if ($existing.Status -ne "Stopped") { Stop-Service $name -Force -ErrorAction SilentlyContinue }
  & sc.exe delete $name | Out-Null
  for ($i = 0; $i -lt 20 -and (Get-Service -Name $name -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
}

$binPath = '"' + $exe + '" --environment Production --Prognode:DataRoot "' + $DataRoot + '"'
New-Service -Name $name -BinaryPathName $binPath -DisplayName "PROGNODE Monitor" -StartupType Automatic `
  -Description "PROGNODE local-first industrial monitoring: device connections, alarms and historian." | Out-Null
# Restart automatically if the service ever stops unexpectedly.
& sc.exe failure $name reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
& sc.exe failureflag $name 1 | Out-Null
Start-Service $name
