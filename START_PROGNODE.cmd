@echo off
setlocal
cd /d "%~dp0"
echo [PROGNODE] Starting the current Core source pilot (not a signed installer).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0START_PROGNODE_RC6_4_7.ps1"
if errorlevel 1 (
  echo [PROGNODE] Startup halted. Review the PowerShell diagnostics; existing data and HTTPS certificates were not intentionally reset.
  pause
  exit /b 1
)
exit /b 0
