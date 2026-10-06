@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0START_PROGNODE_RC6_4_7.ps1"
if errorlevel 1 (
  echo [PROGNODE] HF4.1 startup failed. Do not test an older stale Core instance.
  pause
  exit /b 1
)
