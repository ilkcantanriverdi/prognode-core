$ErrorActionPreference='Stop'
$root=$PSScriptRoot
Set-Location -LiteralPath $root
Write-Host 'PROGNODE HF6.2 / Windows source acceptance (no installed service changes)' -ForegroundColor Cyan
foreach($rel in @(
 'LAN_ACCESS_ELEVATED.ps1',
 'ENABLE_PROGNODE_LAN_HTTPS.ps1',
 'START_PROGNODE_RC6_4_7.ps1',
 'SET_PROGNODE_DATA_ROOT.ps1')){
 $file=Join-Path $root $rel
 if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "MISSING: $rel"}
 $tokens=$null;$errors=$null
 [System.Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors)|Out-Null
 if($errors -and $errors.Count){$errors|ForEach-Object{Write-Error $_};throw "Powershell syntax failed: $rel"}
 Write-Host "[PASS] PS syntax: $rel" -ForegroundColor Green
}
foreach($rel in @(
 'START_PROGNODE.cmd',
 'src/Prognode.Host/wwwroot/index.html',
 'src/Prognode.Host/wwwroot/customer-hf61.css',
 'src/Prognode.Host/wwwroot/trend-hf3plus.html',
 'src/Prognode.Host/wwwroot/trend-hf3plus.js',
 'src/Prognode.Host/wwwroot/trend-hf61.css',
 'src/Prognode.Host/wwwroot/app.js',
 'src/Prognode.Host/Program.cs')){
 if(-not (Test-Path -LiteralPath (Join-Path $root $rel) -PathType Leaf)){throw "MISSING: $rel"}
}
$qrText=[IO.File]::ReadAllText((Join-Path $root 'src\Prognode.Host\wwwroot\app.js'))
if($qrText.Contains('START_PROGNODE_RC6_4_7_HF2.cmd')){throw 'Stale versioned QR user instructions remain in app.js'}
$hostText=[IO.File]::ReadAllText((Join-Path $root 'src\Prognode.Host\Program.cs'))
if(-not $hostText.Contains('lan-https.json')){throw 'Persistent HTTPS config was not linked to Core startup'}
Write-Host '[PASS] HF6.2 UI / TLS configuration source checks.' -ForegroundColor Green
$dotnet=Get-Command dotnet -ErrorAction SilentlyContinue
if(-not $dotnet){throw '.NET 10 SDK is required to compile and run the Windows acceptance tests.'}
& $dotnet.Source build (Join-Path $root 'PROGNODE.sln') -c Debug --nologo
if($LASTEXITCODE -ne 0){throw 'HF6.2 .NET build failed. Do not install this package.'}
Write-Host '[PASS] HF6.2 .NET build. Manual LAN TLS, real QR/ACK and PLC Historian acceptance STILL required.' -ForegroundColor Green
