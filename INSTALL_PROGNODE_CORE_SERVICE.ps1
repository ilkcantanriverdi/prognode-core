param(
  [string]$InstallRoot = "C:\Program Files\PROGNODE",
  [string]$DataRoot = "C:\ProgramData\PROGNODE\data",
  [int]$Port = 5080
)
$ErrorActionPreference = "Stop"
$serviceName = "PROGNODECore"
$publishRoot = Join-Path $InstallRoot "Core"
Write-Host "Publishing PROGNODE Core..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null

dotnet publish "$PSScriptRoot\src\Prognode.Host\Prognode.Host.csproj" -c Release -r win-x64 --self-contained true -o $publishRoot

$exe = Join-Path $publishRoot "Prognode.Host.exe"
if (!(Test-Path $exe)) { throw "Published service executable not found: $exe" }

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
  Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
  sc.exe delete $serviceName | Out-Null
  Start-Sleep -Seconds 2
}

$binPath = '"' + $exe + '" --urls http://127.0.0.1:' + $Port + ' --environment Production --Prognode:DataRoot "' + $DataRoot + '"'
sc.exe create $serviceName binPath= $binPath start= auto DisplayName= "PROGNODE Core" | Out-Null
sc.exe description $serviceName "PROGNODE local-first industrial monitoring Core service." | Out-Null
Start-Service $serviceName
Write-Host "PROGNODE Core service installed and started." -ForegroundColor Green
Write-Host "Core: http://127.0.0.1:$Port  Data: $DataRoot"
