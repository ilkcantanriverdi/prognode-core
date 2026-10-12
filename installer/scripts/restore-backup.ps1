param(
  [string]$BackupFile = "",
  [string]$DataRoot = ""
)
# Restores an encrypted .pgnbackup into the installed PROGNODE Core (Start menu > PROGNODE >
# Restore PROGNODE Backup). Verifies the archive first, keeps the current data as
# data.before-restore-<timestamp>, and uses PROGNODE.Core.exe's offline backup mode.
$ErrorActionPreference = "Stop"
$name = "PROGNODECore"
$installRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $installRoot "Core\PROGNODE.Core.exe"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
  if ($BackupFile) { $argList += " -BackupFile `"$BackupFile`"" }
  if ($DataRoot) { $argList += " -DataRoot `"$DataRoot`"" }
  Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
  exit
}

function Invoke-Core([string[]]$arguments) {
  $output = & $exe backup @arguments 2>&1
  $output | ForEach-Object { Write-Host $_ }
  if ($LASTEXITCODE -ne 0) { throw "PROGNODE Core reported an error (exit code $LASTEXITCODE)." }
  return $output
}

$stoppedService = $false
try {
  Write-Host "PROGNODE backup restore" -ForegroundColor Cyan
  if (!(Test-Path $exe)) { throw "PROGNODE Core executable not found: $exe" }

  $service = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
  if (!$DataRoot) {
    if ($service -and $service.PathName -match '--Prognode:DataRoot "([^"]+)"') { $DataRoot = $Matches[1] }
    else { $DataRoot = Join-Path $env:ProgramData "PROGNODE\data" }
  }
  Write-Host "Data directory: $DataRoot"

  if (!$BackupFile) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = "Select the PROGNODE backup to restore"
    $dialog.Filter = "PROGNODE backup (*.pgnbackup)|*.pgnbackup"
    $backups = Join-Path (Split-Path $DataRoot -Parent) "backups"
    if (Test-Path $backups) { $dialog.InitialDirectory = $backups }
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw "No backup selected; nothing was changed." }
    $BackupFile = $dialog.FileName
  }
  if (!(Test-Path -LiteralPath $BackupFile -PathType Leaf)) { throw "Backup file not found: $BackupFile" }
  Write-Host "Backup file: $BackupFile"

  $secure = Read-Host "Backup passphrase" -AsSecureString
  $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
  try { $env:PROGNODE_BACKUP_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }

  Write-Host "Verifying the encrypted backup..."
  $metaJson = Invoke-Core @("verify", $BackupFile, "--data", $DataRoot, "--json")
  $meta = ($metaJson | Select-Object -Last 1) | ConvertFrom-Json

  $extra = @()
  $currentId = ""
  $identityPath = Join-Path $DataRoot "server-access.json"
  if (Test-Path -LiteralPath $identityPath) {
    try { $currentId = (Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json).serverId } catch {}
  }
  if ($currentId -and $meta.ServerId -and $currentId -ine $meta.ServerId) {
    Write-Warning "This backup comes from a different PROGNODE Core (Server ID $($meta.ServerId))."
    Write-Warning "Its identity and paired devices will replace this PC's. The HTTPS key, the license and Remote Access do NOT move with it."
    if ((Read-Host "To adopt the backup's identity type MIGRATE") -cne "MIGRATE") { throw "Cancelled; nothing was changed." }
    $extra = @("--adopt-source-identity")
  }

  Write-Host ""
  Write-Host "The current data is kept as data.before-restore-<timestamp> next to the data directory."
  if ((Read-Host "Type RESTORE to stop PROGNODE Core and restore this backup") -cne "RESTORE") { throw "Cancelled; nothing was changed." }

  if ($service -and $service.State -ne "Stopped") {
    Write-Host "Stopping the PROGNODE Core service..."
    Stop-Service -Name $name -Force
    (Get-Service -Name $name).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(60))
    $stoppedService = $true
  }

  Invoke-Core (@("restore", $BackupFile, "--data", $DataRoot) + $extra) | Out-Null
  Write-Host "Restore complete." -ForegroundColor Green

  $stoppedService = $false
  if ($service -and (Read-Host "Start PROGNODE Core now? (Y/n)") -notmatch '^[nN]') {
    Start-Service -Name $name
    Write-Host "PROGNODE Core started. Check settings, license and paired devices in PROGNODE." -ForegroundColor Green
  }
}
catch {
  Write-Host $_.Exception.Message -ForegroundColor Red
  # The restore keeps or puts back the previous data on failure, so bring Core back up.
  if ($stoppedService) { Start-Service -Name $name -ErrorAction SilentlyContinue; Write-Host "PROGNODE Core was started again with the previous data." }
}
finally {
  Remove-Item Env:\PROGNODE_BACKUP_PASSWORD -ErrorAction SilentlyContinue
  Read-Host "Press Enter to close" | Out-Null
}
