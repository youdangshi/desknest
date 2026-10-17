@echo off
rem 启动桌巢：run.cmd [Debug|Release]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
