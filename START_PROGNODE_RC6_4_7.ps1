$ErrorActionPreference = 'Stop'
$expected = '0.7.2-rc6.4.7-hf6.3-auto-lan-trend'
$root = $PSScriptRoot
Set-Location $root

# HF4.1.2.1: Refuse to touch the running Core or create a backup with a
# syntactically broken elevated helper. This uses the Windows PowerShell 5.1
# parser and does not require administrator rights or invoke the helper.
$lanHelper=Join-Path $root 'LAN_ACCESS_ELEVATED.ps1'
if (-not (Test-Path -LiteralPath $lanHelper -PathType Leaf)) {
    throw "Missing required LAN setup helper: $lanHelper"
}
$helperTokens=$null
$helperSyntaxErrors=$null
[System.Management.Automation.Language.Parser]::ParseFile(
    $lanHelper, [ref]$helperTokens, [ref]$helperSyntaxErrors) | Out-Null
if ($null -ne $helperSyntaxErrors -and $helperSyntaxErrors.Count -gt 0) {
    $helperSyntaxErrors | ForEach-Object { Write-Error ("LAN helper syntax: " + $_.ToString()) }
    throw 'LAN_ACCESS_ELEVATED.ps1 contains PowerShell syntax errors; no Core/Agent upgrade was started.'
}
Write-Host '[PASS] LAN_ACCESS_ELEVATED.ps1: PowerShell syntax checked before upgrade.' -ForegroundColor Green

# A stale tray Agent produces the original empty-JSON popup even if Core is updated.
# Refuse to leave an older Agent running while producing new binaries. Let the
# customer exit it explicitly rather than terminating unrelated installed Agents.
$oldAgents = @(Get-Process -Name 'PROGNODE.Agent' -ErrorAction SilentlyContinue)
if ($oldAgents.Count -gt 0) {
    $oldAgents | ForEach-Object { Write-Warning ("Tray Agent PID " + $_.Id + " is still running. Exit it via its tray menu before the HF4.1.2 upgrade.") }
    throw 'Close ALL running PROGNODE tray Agents (Exit PROGNODE Agent), then relaunch this script to avoid locked files/stale code.'
}

# Pre-upgrade safety snapshot happens BEFORE stopping the previous Core. The SQLite
# snapshot API is WAL-safe even while the previous Core is writing Historian samples.
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw '.NET 10 SDK is required. The existing Core was not stopped.' }
# HF6.2: an extracted source folder is NOT the customer's installation identity.
# First use must explicitly configure new or existing data; upgrades reuse the
# previous per-user pointer. Never silently create an empty database after unzip.
$dataPointerDir = Join-Path $env:LOCALAPPDATA 'PROGNODE'
$dataPointer = Join-Path $dataPointerDir 'dev-data-root.txt'
$selectedFromPointer = $false
if (-not $env:Prognode__DataRoot) {
    if (Test-Path -LiteralPath $dataPointer -PathType Leaf) {
        $savedDataPath = [string](Get-Content -LiteralPath $dataPointer -Raw -ErrorAction Stop).Trim()
        if ([string]::IsNullOrWhiteSpace($savedDataPath)) { throw 'Stored PROGNODE data path is empty; run SET_PROGNODE_DATA_ROOT.ps1.' }
        $env:Prognode__DataRoot = $savedDataPath
        $selectedFromPointer = $true
    } elseif (Test-Path -LiteralPath (Join-Path $root 'data\prognode.db') -PathType Leaf) {
        # In-place old source upgrade only. Remember it for subsequent ZIP releases.
        $env:Prognode__DataRoot = Join-Path $root 'data'
        New-Item -ItemType Directory -Force -Path $dataPointerDir | Out-Null
        [IO.File]::WriteAllText($dataPointer, [IO.Path]::GetFullPath($env:Prognode__DataRoot))
        $selectedFromPointer = $true
    } else {
        throw 'DATA_ROOT_NOT_CONFIGURED: No existing PROGNODE project was selected. Run SET_PROGNODE_DATA_ROOT.ps1 -Path "C:\old-prognode\data" to reuse the existing database, OR SET_PROGNODE_DATA_ROOT.ps1 -CreateNew for a clean pilot PC. The old Core has NOT been stopped.'
    }
}
$data = [IO.Path]::GetFullPath($env:Prognode__DataRoot)
if ($selectedFromPointer -and -not (Test-Path -LiteralPath (Join-Path $data 'prognode.db') -PathType Leaf)) {
    # A freshly and explicitly provisioned empty pilot folder is allowed once.
    $newMarker = Join-Path $data '.prognode-first-run-approved'
    if (-not (Test-Path -LiteralPath $newMarker -PathType Leaf)) {
        throw "DATA_ROOT_MISSING: $data contains no prognode.db. Do not start an empty project or overwrite customer data. Restore your previous data folder or run SET_PROGNODE_DATA_ROOT.ps1."
    }
    Write-Host ('[PROGNODE] Explicit clean pilot first-run: ' + $data) -ForegroundColor Cyan
}

$oldDb = Join-Path $data 'prognode.db'
if (Test-Path $oldDb) {
  $bkDir = Join-Path (Split-Path $data -Parent) 'backups'
  New-Item -ItemType Directory -Force -Path $bkDir | Out-Null
  $existing = @(Get-ChildItem $bkDir -Filter 'PREUPGRADE-HF4.1-*.pgnbackup' -ErrorAction SilentlyContinue)
  if ($existing.Count -eq 0) {
    $backupPassWasPrompted=$false
    if (-not $env:PROGNODE_BACKUP_PASSWORD -or $env:PROGNODE_BACKUP_PASSWORD.Length -lt 12) {
      Write-Host '[PROGNODE] First HF4.1 start: encryption password needed for the pre-upgrade snapshot.' -ForegroundColor Cyan
      $secure = Read-Host 'Backup passphrase (at least 12 characters; keep it safe)' -AsSecureString
      $ptr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
      try { $env:PROGNODE_BACKUP_PASSWORD=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
      finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
      $backupPassWasPrompted=$true
    }
    if ($env:PROGNODE_BACKUP_PASSWORD.Length -lt 12) { throw 'Pre-upgrade backup password must have 12+ characters. Old Core left intact.' }
    $backupFile=Join-Path $bkDir ('PREUPGRADE-HF4.1-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N')+'.pgnbackup')
    Write-Host '[PROGNODE] Making a verified encrypted snapshot before upgrade...' -ForegroundColor Cyan
    try {
      & dotnet run --project (Join-Path $root 'tools\Prognode.Backup.Restore\Prognode.Backup.Restore.csproj') -- create $backupFile --data $data
      if($LASTEXITCODE -ne 0){throw 'Pre-upgrade snapshot failed. Old Core was not stopped.'}
    } finally {
      if($backupPassWasPrompted){Remove-Item Env:\PROGNODE_BACKUP_PASSWORD -ErrorAction SilentlyContinue}
    }
    Write-Host "[PASS] Pre-upgrade backup saved: $backupFile" -ForegroundColor Green
  }
}

Write-Host '[PROGNODE] Checking and stopping the old Core on TCP 5080...' -ForegroundColor Cyan
$svc = Get-Service -Name 'PROGNODECore' -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Stopped') {
    try {
        Stop-Service -Name 'PROGNODECore' -Force -ErrorAction Stop
        (Get-Service -Name 'PROGNODECore').WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    } catch { throw 'The installed PROGNODECore service must be stopped. Run this script as Administrator or stop the service manually. '+$_.Exception.Message }
}
$occupied = @(Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue | Sort-Object OwningProcess -Unique)
foreach ($connection in $occupied) {
    $pidToCheck = [int]$connection.OwningProcess
    $oldProcess = Get-CimInstance Win32_Process -Filter ("ProcessId=$pidToCheck") -ErrorAction SilentlyContinue
    if (-not $oldProcess) { throw "Port 5080 belongs to PID $pidToCheck; stop it before starting RC6.4.7." }
    $isPrognode = ($oldProcess.Name -match '^Prognode\.Host(\.exe)?$') -or
                  (($oldProcess.Name -match '^dotnet(\.exe)?$') -and ($oldProcess.CommandLine -match 'Prognode\.Host'))
    if (-not $isPrognode) { throw "Port 5080 belongs to $($oldProcess.Name) (PID $pidToCheck); will not stop unrelated software." }
    Write-Host "[PROGNODE] Stopping old developer Core PID $pidToCheck."
    Stop-Process -Id $pidToCheck -Force -ErrorAction Stop
}
Start-Sleep -Seconds 2
if (Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 5080 is still occupied. Resolve it before starting the new Core.'
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw '.NET 10 SDK is required to compile this source package.' }
Write-Host '[PROGNODE] Building RC6.4.7 HF6.3 (auto LAN startup + Historian catalog + signed-in Trend editing)...' -ForegroundColor Cyan
& dotnet build (Join-Path $root 'PROGNODE.sln') -c Debug --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Do not start the older Core as a fallback.' }
$dll = Join-Path $root 'src\Prognode.Host\bin\Debug\net10.0\Prognode.Host.dll'
if (-not (Test-Path $dll)) { throw "New Core DLL not found: $dll" }

# Data root was resolved and checked before any running service was stopped.
$env:ASPNETCORE_ENVIRONMENT = 'Development'
# Kestrel binds plain HTTP to localhost; LAN clients use the separately configured HTTPS listener.
$env:ASPNETCORE_CONTENTROOT = Join-Path $root 'src\Prognode.Host'
Write-Host ("[PROGNODE] Data root: " + $env:Prognode__DataRoot)
Write-Host '[PROGNODE] Launching the newly compiled Core...' -ForegroundColor Cyan
$logOut = Join-Path $root 'RC647_CORE_STDOUT.log'
$logErr = Join-Path $root 'RC647_CORE_STDERR.log'
# The developer pilot uses the same process identity and data root across LAN
# repairs. Service installs instead get an SCM-controlled Restart-Service.
$supervisorScript=Join-Path $root 'START_PROGNODE_RUNTIME.ps1'
$env:PROGNODE_CORE_SUPERVISED='1'
$p = Start-Process -FilePath 'powershell.exe' -WorkingDirectory $root -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File','"'+$supervisorScript+'"',
    '-ProjectRoot','"'+$root+'"','-CoreDll','"'+$dll+'"',
    '-DataRoot','"'+$data+'"','-ExpectedVersion','"'+$expected+'"') -PassThru
$ok = $false
for ($i=0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if ($p.HasExited) { break }
    try {
        $h = Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/health' -TimeoutSec 2
        if ($h.coreVersion -eq $expected) { $ok=$true; break }
        throw "Wrong Core is serving port 5080: $($h.coreVersion); expected $expected"
    } catch {
        if ($_.Exception.Message -match 'Wrong Core') { throw }
    }
}
if (-not $ok) {
    if (Test-Path $logErr) { Get-Content $logErr -Tail 25 }
    if (Test-Path $logOut) { Get-Content $logOut -Tail 25 }
    throw 'New Core failed to expose its expected health version on port 5080.'
}
try {
    $trend = Invoke-WebRequest -Uri 'http://127.0.0.1:5080/trend-hf3plus.html' -TimeoutSec 6
    if ($trend.StatusCode -ne 200 -or $trend.Content -notmatch 'trend-hf3plus.js') { throw 'Integrated HF3+ Trend Studio is not being served.' }
} catch { throw 'Core is running but integrated Trend Studio could not be verified: '+$_.Exception.Message }
try {
    $qrJs = Invoke-WebRequest -Uri 'http://127.0.0.1:5080/qr-local.js' -TimeoutSec 6
    if ($qrJs.StatusCode -ne 200 -or $qrJs.Content -notmatch 'PrognodeQrSvg') {throw 'New QR renderer not available.'}
    $qrUi = Invoke-WebRequest -Uri 'http://127.0.0.1:5080/' -TimeoutSec 6
    if ($qrUi.Content -notmatch 'qrPairStart') {throw 'New QR Settings UI not available.'}
    if ($qrUi.Content -notmatch '/trend-hf3plus.html') {throw 'Native Core still mounts an older Trend Studio page.'}
} catch {throw 'QR pairing UI smoke test failed: '+$_.Exception.Message}
# A clean pilot marker is one-use: once database creation succeeds, remove it.
$firstRunMarker=Join-Path $data '.prognode-first-run-approved'
if ((Test-Path -LiteralPath $firstRunMarker -PathType Leaf) -and
    (Test-Path -LiteralPath (Join-Path $data 'prognode.db') -PathType Leaf)) {
    Remove-Item -LiteralPath $firstRunMarker -Force -ErrorAction Stop
}
Write-Host '[PASS] PROGNODE Core, real Trend Studio and QR pairing UI are being served.' -ForegroundColor Green
try {
    $identity = Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/server/identity' -TimeoutSec 6
    if ($identity.secureApiPort) {
        Write-Host ("[TLS CONFIGURED — VERIFY BEFORE QR] LAN HTTPS port: " + $identity.secureApiPort) -ForegroundColor Green
    } else {
        Write-Warning 'QR needs verified LAN HTTPS. Open Settings > Mobile LAN > Verify and use the local Agent UAC repair if required. Do not replace a working certificate.'
    }
} catch { Write-Warning 'Cannot read LAN HTTPS state; open Settings / Client Access for diagnostics.' }
$agent = Join-Path $root 'src\Prognode.Agent.Windows\bin\Debug\net10.0-windows10.0.19041.0\PROGNODE.Agent.exe'
if (Test-Path $agent) {
    Start-Process -FilePath $agent -WorkingDirectory (Split-Path $agent -Parent)
    Write-Host '[PROGNODE] Windows Agent started; use its tray menu for LAN setup UAC consent.' -ForegroundColor Green
} else {Write-Warning 'Windows Agent binary not found; launch START_PROGNODE_SERVER_AGENT.cmd and try again.'}
Write-Host '[PROGNODE] Backup Center: Settings; automatic backup needs PROGNODE_BACKUP_PASSWORD in the Core service environment.' 
Write-Host '[PROGNODE] Open the browser; if it shows old UI, use Ctrl+F5 in the browser.'
Start-Process 'http://127.0.0.1:5080/?build=hf63-auto-lan-trend'
