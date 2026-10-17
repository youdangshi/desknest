@echo off
rem DeskNest build: build.cmd [Debug or Release] [--smoke]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
pause
