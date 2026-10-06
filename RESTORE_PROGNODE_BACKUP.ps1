param(
  [Parameter(Mandatory=$false)][string]$BackupFile = "",
  [Parameter(Mandatory=$false)][string]$DataRoot = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if(-not $DataRoot){
  $default = Join-Path $env:ProgramData 'PROGNODE\data'
  if(Test-Path (Join-Path $root 'data\prognode.db')) { $default = Join-Path $root 'data' }
  $DataRoot = Read-Host "Full data directory [$default]"
  if(-not $DataRoot){$DataRoot=$default}
}
if(-not $BackupFile){ $BackupFile = Read-Host "Full .pgnbackup file path" }
if(-not (Test-Path -LiteralPath $BackupFile -PathType Leaf)){throw "Backup file not found"}
$svc = Get-Service -Name 'PROGNODECore' -ErrorAction SilentlyContinue
if($svc -and $svc.Status -ne 'Stopped'){
  Write-Warning 'PROGNODE Core service must be stopped before restore.'
  $confirm = Read-Host 'Stop PROGNODE Core service? type YES'
  if($confirm -cne 'YES'){throw 'Cancelled'}
  Stop-Service -Name 'PROGNODECore' -ErrorAction Stop
  $svc.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(40))
}
try {
  $conn = Test-NetConnection 127.0.0.1 -Port 5080 -InformationLevel Quiet -WarningAction SilentlyContinue
  if($conn){throw 'Port 5080 is still active; close developer Core and retry.'}
} catch { if($_.Exception.Message -like '*Port 5080*'){throw} }
$secure=Read-Host 'Backup passphrase' -AsSecureString
$ptr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $env:PROGNODE_BACKUP_PASSWORD=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
try {
  Write-Host "Verifying encrypted backup..."
  dotnet run --project (Join-Path $root 'tools\Prognode.Backup.Restore\Prognode.Backup.Restore.csproj') -- verify $BackupFile --data $DataRoot
  if($LASTEXITCODE -ne 0){throw 'Backup validation failed; existing Core data was NOT touched.'}
  $metaJson = dotnet run --project (Join-Path $root 'tools\Prognode.Backup.Restore\Prognode.Backup.Restore.csproj') -- verify $BackupFile --data $DataRoot --json
  if($LASTEXITCODE -ne 0){throw 'Backup metadata validation failed.'}
  $meta = $metaJson | Select-Object -Last 1 | ConvertFrom-Json
  $currentId=''
  $identityPath=Join-Path $DataRoot 'server-access.json'
  if(Test-Path -LiteralPath $identityPath){try{$currentId=(Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json).serverId}catch{}}
  $crossServer=[bool]($currentId -and $meta.ServerId -and $currentId -ine $meta.ServerId)
  $extra=@()
  if($crossServer){
    Write-Warning 'Different Server ID. Verified archive is NOT corrupt; this is a separate identity migration.'
    Write-Warning 'Source identity and paired-device records will replace this Core data. The HTTPS private key, machine-bound license and remote entitlement DO NOT migrate automatically.'
    Write-Host "CURRENT: $currentId | SOURCE: $($meta.ServerId)"
    if((Read-Host 'To adopt the backup identity type MIGRATE') -cne 'MIGRATE'){throw 'Migration declined; existing Core untouched.'}
    if((Read-Host "Type the entire SOURCE Server ID $($meta.ServerId)") -cne $meta.ServerId){throw 'Source Server ID does not match; restore cancelled.'}
    $extra=@('--adopt-source-identity')
  }
  Write-Host 'IMPORTANT: Current data will be preserved in data.before-restore-TIMESTAMP.'
  if((Read-Host 'Type RESTORE to replace current data directory') -cne 'RESTORE'){ throw 'Cancelled; existing data unchanged.' }
  dotnet run --project (Join-Path $root 'tools\Prognode.Backup.Restore\Prognode.Backup.Restore.csproj') -- restore $BackupFile --data $DataRoot @extra
  if($LASTEXITCODE -ne 0){ throw 'Restore failed; examine the preserved original data folder.' }
  Write-Host 'Restore complete; inspect settings and license before starting Core.'
} finally {Remove-Item Env:\PROGNODE_BACKUP_PASSWORD -ErrorAction SilentlyContinue}
