# Stops and removes the PROGNODE Core Windows service. Data in ProgramData is kept.
$ErrorActionPreference = "SilentlyContinue"
$name = "PROGNODECore"
if (Get-Service -Name $name) {
  Stop-Service $name -Force
  & sc.exe delete $name | Out-Null
}
Get-Process -Name "PROGNODE.Agent" | Stop-Process -Force
