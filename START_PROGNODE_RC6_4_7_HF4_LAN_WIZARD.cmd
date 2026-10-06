@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0START_PROGNODE_RC6_4_7.ps1"
if errorlevel 1 (
  echo.
  echo [PROGNODE] HF4 startup failed. Do not use a stale Core session.
  pause
  exit /b 1
)
