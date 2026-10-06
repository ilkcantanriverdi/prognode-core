@echo off
setlocal
cd /d "%~dp0"
set EXPECTED_VERSION=0.7.2-rc6.4.2-trend-analysis
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://0.0.0.0:5080
set PROGNODE_CORE_URL=http://127.0.0.1:5080

echo [PROGNODE] Building RC6.4.2 Trend Analysis Preview...
dotnet build PROGNODE.sln
if errorlevel 1 goto :fail

echo [PROGNODE] Stopping an installed PROGNODECore service for this developer run, if present...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=Get-Service -Name 'PROGNODECore' -ErrorAction SilentlyContinue; if($s -and $s.Status -eq 'Running'){Stop-Service -Name 'PROGNODECore' -Force; Start-Sleep -Seconds 1}"

echo [PROGNODE] Releasing port 5080 from an older developer Core, if present...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$c=Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1; if($c){Write-Host ('Stopping PID ' + $c.OwningProcess + ' on port 5080'); Stop-Process -Id $c.OwningProcess -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1}"

echo [PROGNODE] Starting Windows Agent...
start "PROGNODE Agent RC6.4.2" "src\Prognode.Agent.Windows\bin\Debug\net10.0-windows10.0.19041.0\PROGNODE.Agent.exe"

echo [PROGNODE] Starting RC6.4.2 Core on http://127.0.0.1:5080
start "PROGNODE Server RC6.4.2" cmd /k "set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://0.0.0.0:5080&& dotnet src\Prognode.Host\bin\Debug\net10.0\Prognode.Host.dll"

timeout /t 3 /nobreak >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0VERIFY_PROGNODE_RC6_4.ps1"
if errorlevel 1 goto :wrongversion
start "" "http://127.0.0.1:5080/?build=rc642trendanalysis"
exit /b 0

:wrongversion
echo.
echo [PROGNODE] The correct RC6.4.2 Core is NOT serving port 5080.
echo Close the old PROGNODE process/service and run this script again.
pause
exit /b 2

:fail
echo.
echo PROGNODE build failed. Review the errors above.
pause
exit /b 1
