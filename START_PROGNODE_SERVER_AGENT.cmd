@echo off
setlocal
cd /d "%~dp0"
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://0.0.0.0:5080
set PROGNODE_CORE_URL=http://127.0.0.1:5080

echo [PROGNODE] Building Server + Agent...
dotnet build PROGNODE.sln
if errorlevel 1 goto :fail

echo [PROGNODE] Starting Windows Agent...
start "PROGNODE Agent" "src\Prognode.Agent.Windows\bin\Debug\net10.0-windows10.0.19041.0\PROGNODE.Agent.exe"

echo [PROGNODE] Starting Server/Core on http://127.0.0.1:5080
start "PROGNODE Server" cmd /k "set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://0.0.0.0:5080&& dotnet src\Prognode.Host\bin\Debug\net10.0\Prognode.Host.dll"

timeout /t 2 /nobreak >nul
start "" http://127.0.0.1:5080
exit /b 0

:fail
echo.
echo PROGNODE build failed. Review the errors above.
pause
exit /b 1
