@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Project.ps1" -Action Editor
if errorlevel 1 pause
endlocal
