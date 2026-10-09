param(
  [Parameter(Mandatory = $true)][string]$InstallRoot,
  [Parameter(Mandatory = $true)][string]$DataRoot
)
# Registers (or re-registers) the PROGNODE Core Windows service and starts it.
$ErrorActionPreference = "Stop"
$name = "PROGNODECore"
$exe = Join-Path $InstallRoot "Core\PROGNODE.Core.exe"
if (!(Test-Path $exe)) { throw "PROGNODE Core executable not found: $exe" }
New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null

$existing = Get-Service -Name $name -ErrorAction SilentlyContinue
if ($existing) {
  if ($existing.Status -ne "Stopped") { Stop-Service $name -Force -ErrorAction SilentlyContinue }
  & sc.exe delete $name | Out-Null
  for ($i = 0; $i -lt 20 -and (Get-Service -Name $name -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
}

$binPath = '"' + $exe + '" --environment Production --Prognode:DataRoot "' + $DataRoot + '"'
New-Service -Name $name -BinaryPathName $binPath -DisplayName "PROGNODE Core" -StartupType Automatic `
  -Description "PROGNODE local-first industrial monitoring: device connections, alarms and historian." | Out-Null
# Restart automatically if the service ever stops unexpectedly.
& sc.exe failure $name reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
& sc.exe failureflag $name 1 | Out-Null
Start-Service $name
