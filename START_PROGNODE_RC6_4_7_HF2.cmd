@echo off
setlocal
PowerShell -NoProfile -ExecutionPolicy Bypass -File "%~dp0START_PROGNODE_RC6_4_7.ps1"
if errorlevel 1 ( echo [ERROR] HF2 QR + HF3+ failed to build or start. & pause & exit /b 1 )
