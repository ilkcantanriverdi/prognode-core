# Run in a PowerShell terminal on the Windows development PC with .NET 10 SDK installed.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK bulunamadi. https://dotnet.microsoft.com/download/dotnet/10.0'
}
Write-Host '--- SDK ---'
dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'dotnet --version failed' }
Write-Host '--- Backup + LAN/HTTPS compilation ---'
dotnet build .\src\Prognode.Backup\Prognode.Backup.csproj -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Backup compile failed' }
dotnet build .\src\Prognode.Web\Prognode.Web.csproj -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Web compile failed' }
Write-Host '--- Full solution compilation ---'
dotnet build .\PROGNODE.sln -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Solution compile failed' }
Write-Host '--- Offline backup contract test ---'
dotnet run --project .\tests\BackupContract\BackupContract.csproj -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Backup contract test failed' }
Write-Host 'HF4.1.1 build + backup contract PASS' -ForegroundColor Green
