@echo off
rem Build + pack + register + launch the WinUI 3 shell (one click).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\pack-shell.ps1" -Install
echo.
pause
