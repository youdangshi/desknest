@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\pack-msix.ps1"
echo.
pause
