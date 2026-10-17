@echo off
rem DeskNest launcher: run.cmd [Debug or Release]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
