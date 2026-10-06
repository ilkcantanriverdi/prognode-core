@echo off
setlocal
PowerShell -NoProfile -ExecutionPolicy Bypass -File "%~dp0START_PROGNODE_RC6_4_7.ps1"
if errorlevel 1 (
  echo [ERROR] RC6.4.7 HF2 QR + HF3+ Core did not start. See the errors above.
  pause
  exit /b 1
)
