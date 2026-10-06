@echo off
cd /d "%~dp0"
echo Select an EXISTING project from PowerShell with SET_PROGNODE_DATA_ROOT.ps1 -Path "C:\old\data".
echo Or clean pilot: powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0SET_PROGNODE_DATA_ROOT.ps1" -CreateNew
pause
