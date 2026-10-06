@echo off
setlocal
cd /d "%~dp0"
echo [PROGNODE] Cleaning Core/Server solution...
dotnet clean PROGNODE.sln
if errorlevel 1 goto :fail
echo [PROGNODE] Building Core/Server solution...
dotnet build PROGNODE.sln
if errorlevel 1 goto :fail
echo.
echo Build succeeded.
pause
exit /b 0
:fail
echo.
echo PROGNODE build failed. Review the errors above.
pause
exit /b 1
