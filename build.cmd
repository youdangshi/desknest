@echo off
rem 桌巢 DeskNest 一键编译（双击即可）：build.cmd [Debug|Release] [--smoke]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
pause
