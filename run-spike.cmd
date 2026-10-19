@echo off
setlocal enabledelayedexpansion
set "EXE=%~dp0spike-winui\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Spike.exe"
set "DIR=%~dp0spike-winui\bin\Debug\net10.0-windows10.0.19041.0\win-x64"
echo ==== P0 spike: 3 modes, 12s each ====
if not exist "%EXE%" (echo [FAILED] exe not found & pause & exit /b 1)
if exist "%~dp0_spike-*.log" del "%~dp0_spike-*.log"
for %%M in (owner workerw bottom) do (
  echo.
  echo ---- %%M ---- (now: click the button a few times if you can)
  if exist "%DIR%\spike.log" del "%DIR%\spike.log"
  start "" "%EXE%" %%M
  timeout /t 12 /nobreak >nul
  taskkill /f /im Spike.exe >nul 2>nul
  if exist "%DIR%\spike.log" (
    copy /y "%DIR%\spike.log" "%~dp0_spike-%%M.log" >nul
    echo ---- log %%M ----
    type "%~dp0_spike-%%M.log"
  ) else (
    echo [no log] %%M: app exited before writing
  )
)
echo.
echo ==== done. logs saved as _spike-*.log in the project folder ====
pause
