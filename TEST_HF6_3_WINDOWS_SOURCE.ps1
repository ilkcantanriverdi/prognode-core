# HF6.3 source validation. Does not install, stop or change an existing service.
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
Set-Location -LiteralPath $root
foreach($rel in @('LAN_ACCESS_ELEVATED.ps1','START_PROGNODE_RC6_4_7.ps1','START_PROGNODE_RUNTIME.ps1','SET_PROGNODE_DATA_ROOT.ps1')){
  $file=Join-Path $root $rel
  if(-not (Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing: $rel"}
  $tokens=$null;$errors=$null
  [System.Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors)|Out-Null
  if($errors -and $errors.Count){$errors|ForEach-Object{Write-Error $_};throw "PS syntax failed: $rel"}
  Write-Host "[PASS] PowerShell 5.1 syntax: $rel" -ForegroundColor Green
}
$hostCode=[IO.File]::ReadAllText((Join-Path $root 'src\Prognode.Host\Program.cs'))
$trendCode=[IO.File]::ReadAllText((Join-Path $root 'src\Prognode.Host\wwwroot\trend-hf3plus.js'))
$helperCode=[IO.File]::ReadAllText((Join-Path $root 'LAN_ACCESS_ELEVATED.ps1'))
if($hostCode -notmatch 'LanAutoRestartHostedService' -or $helperCode -notmatch 'lan-restart-request.json'){throw 'HF6.3 LAN supervision source missing'}
if($trendCode -notmatch 'refreshTrendAuth' -or $trendCode -notmatch 'pgn:historian-changed'){throw 'HF6.3 Trend access/catalog source missing'}
$dotnet=Get-Command dotnet -ErrorAction SilentlyContinue
if(-not $dotnet){throw '.NET 10 SDK missing; install SDK for this source pilot.'}
& $dotnet.Source build (Join-Path $root 'PROGNODE.sln') -c Debug --nologo
if($LASTEXITCODE -ne 0){throw 'HF6.3 .NET build failed. Existing Core was NOT replaced.'}
Write-Host '[PASS] HF6.3 build / static checks. Perform actual UAC, service restart, QR, ACK and Historian testing before delivery.' -ForegroundColor Green
