@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0RESTORE_PROGNODE_BACKUP.ps1"
pause
