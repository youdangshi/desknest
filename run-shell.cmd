@echo off
rem Open the registered WinUI 3 shell (no rebuild). If not registered yet, run build-shell.cmd once.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run-shell.ps1"
