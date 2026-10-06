@echo off
setlocal
cd /d "%~dp0"
echo [PROGNODE] HF6 acceptance build (not a production installer).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0START_PROGNODE_RC6_4_7.ps1"
if errorlevel 1 (
  echo [PROGNODE] HF6 startup failed. Review the PowerShell output.
  pause
  exit /b 1
)
