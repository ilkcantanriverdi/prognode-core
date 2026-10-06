# PROGNODE HF6.5: safe source contract checks on Windows 11 / .NET 10.
# All tests use isolated temporary data; no production Core files are restored.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if(-not (Get-Command dotnet -ErrorAction SilentlyContinue)){throw ".NET 10 SDK is required on the developer PC."}
function Test-Step([string]$project){
  Write-Host "RUNNING $project" -ForegroundColor Cyan
  dotnet run --project $project -c Debug
  if($LASTEXITCODE -ne 0){throw "FAILED: $project"}
}
dotnet build .\PROGNODE.sln -c Debug
if($LASTEXITCODE -ne 0){throw "HF6.5 solution build FAILED"}
Test-Step '.\tests\QrPairingCoreContract\QrPairingCoreContract.csproj'
Test-Step '.\tests\ManualPairingContract\ManualPairingContract.csproj'
Test-Step '.\tests\BackupContract\BackupContract.csproj'
Test-Step '.\tests\MobileDeviceAckContract\MobileDeviceAckContract.csproj'
Write-Host 'HF6.5 source contract tests passed. Physical phone and TLS MITM tests still required.' -ForegroundColor Green
