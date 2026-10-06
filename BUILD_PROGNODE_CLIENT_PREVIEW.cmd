@echo off
setlocal
cd /d "%~dp0tools\Prognode.Client.Windows.Preview"
dotnet restore Prognode.Client.Windows.Preview.csproj
if errorlevel 1 exit /b %errorlevel%
dotnet build Prognode.Client.Windows.Preview.csproj -c Release
if errorlevel 1 exit /b %errorlevel%
echo.
echo Build complete.
echo EXE is under tools\Prognode.Client.Windows.Preview\bin\Release\net10.0-windows10.0.19041.0\
