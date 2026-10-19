@echo off
setlocal enabledelayedexpansion
pushd "%~dp0"
set "DOTNET=..\..\.dotnet-sdk\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
set "DOTNET_CLI_HOME=%~dp0.dotnet-home"
set "NUGET_PACKAGES=%~dp0.nuget-winui"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
echo [1/3] restore ...
"%DOTNET%" restore "%~dp0spike-winui\Spike.csproj" -r win-x64 -v:m
if errorlevel 1 (echo [FAILED] restore & pause & popd & exit /b 1)
echo [2/3] build (self-contained, ~200MB) ...
"%DOTNET%" build "%~dp0spike-winui\Spike.csproj" -c Debug --no-restore -m:1 -nr:false -v:m
if errorlevel 1 (echo [FAILED] build & pause & popd & exit /b 1)
set "DIR=%~dp0spike-winui\bin\Debug\net10.0-windows10.0.19041.0\win-x64"
set "EXE=%DIR%\Spike.exe"
if not exist "%EXE%" (echo [FAILED] exe missing & pause & popd & exit /b 1)
echo [3/3] running 3 modes, 12s each - CLICK THE BUTTON when the window appears
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
echo ==== done ====
popd
pause
