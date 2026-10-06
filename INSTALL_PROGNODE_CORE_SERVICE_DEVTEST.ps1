$ErrorActionPreference = "Stop"
$devData = Join-Path $PSScriptRoot "src\Prognode.Host\data"
Write-Host "DEV TEST ONLY: The Windows Service will use the current development database at:" -ForegroundColor Yellow
Write-Host $devData -ForegroundColor Yellow
Write-Host "Stop VS Code / Prognode.Host before continuing. Never run the dev Host and Windows Service against the same SQLite DB at the same time." -ForegroundColor Yellow
$answer = Read-Host "Type YES to continue"
if ($answer -ne "YES") { exit 1 }
& "$PSScriptRoot\INSTALL_PROGNODE_CORE_SERVICE.ps1" -DataRoot $devData
