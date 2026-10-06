@echo off
setlocal
fltmc >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator permission for local LAN HTTPS setup...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
echo Configuring LAN HTTPS without replacing an existing certificate...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ENABLE_PROGNODE_LAN_HTTPS.ps1"
if errorlevel 1 ( echo HTTPS setup failed. Existing configuration was not intentionally reset. & pause & exit /b 1 )
echo.
echo Restart PROGNODE using START_PROGNODE.cmd, then open Settings - Mobile LAN - Verify.
pause
