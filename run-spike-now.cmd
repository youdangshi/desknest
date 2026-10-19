@echo off
setlocal enabledelayedexpansion
set "DIR=%~dp0spike-winui\bin\Debug\net10.0-windows10.0.19041.0\win-x64"
set "EXE=%DIR%\Spike.exe"
if not exist "%EXE%" (echo [FAILED] exe not found: %EXE% & exit /b 1)
echo ==== running 3 modes, 12s each ====
echo CLICK THE BUTTON when a white window appears.
if exist "%~dp0_spike-*.log" del "%~dp0_spike-*.log"
for %%M in (owner workerw bottom) do (
  echo.
  echo ---- %%M ----
  if exist "%DIR%\spike.log" del "%DIR%\spike.log"
  start "" "%EXE%" %%M
  timeout /t 12 /nobreak >nul
  taskkill /f /im Spike.exe >nul 2>nul
  if exist "%DIR%\spike.log" (
    copy /y "%DIR%\spike.log" "%~dp0_spike-%%M.log" >nul
    type "%~dp0_spike-%%M.log"
  ) else (
    echo [no log] %%M
  )
)
echo.
echo ==== done. logs: _spike-*.log ====
