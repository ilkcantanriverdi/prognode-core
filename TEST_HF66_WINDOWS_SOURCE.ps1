$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK is required on test Windows PC.' }
Write-Host 'HF6.6: building the complete solution...'
dotnet build '.\PROGNODE.sln' -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'HF6.6 solution build failed. Keep current working Core untouched.' }
Write-Host 'HF6.6 build successful. Next: actual S7 CPU / DB read, Historian and QR regression.'
