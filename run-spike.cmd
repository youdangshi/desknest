@echo off
setlocal
set "EXE=%~dp0spike-winui\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Spike.exe"
set "LOGDIR=%~dp0spike-winui\bin\Debug\net10.0-windows10.0.19041.0\win-x64"
set "LOG=%LOGDIR%\spike.log"
echo ==== P0 spike: 3 hosting modes ====
echo EXE=%EXE%
if not exist "%EXE%" (echo [FAILED] exe not found & pause & exit /b 1)
if exist "%LOGDIR%\spike.log" del "%LOGDIR%\spike.log"
for %%M in (owner workerw bottom) do (
  echo.
  echo ---- mode: %%M ----
  start "" "%EXE%" %%M
  echo (12 seconds: look at the window, try clicking the button)
  timeout /t 12 /nobreak >nul
  taskkill /f /im Spike.exe >nul 2>nul
  echo ---- log after %%M ----
  if exist "%LOG%" (type "%LOG%") else (echo [no log - app exited before writing])
  if exist "%LOG%" del "%LOG%"
)
echo.
echo ==== done ====
pause
