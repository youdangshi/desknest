@echo off
setlocal
rem repo = C:\Users\<user>\Desktop\DeskNest ; SDK = C:\Users\<user>\.dotnet-sdk
set "DOTNET=%~dp0..\..\.dotnet-sdk\dotnet.exe"
if not exist "%DOTNET%" (
  echo [WARN] SDK not found at %DOTNET%, falling back to PATH dotnet
  set "DOTNET=dotnet"
)
set "DOTNET_CLI_HOME=%~dp0.dotnet-home"
set "NUGET_PACKAGES=%~dp0.nuget-winui"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
echo DOTNET=%DOTNET%
if not exist "%DOTNET_CLI_HOME%" mkdir "%DOTNET_CLI_HOME%"
if not exist "%NUGET_PACKAGES%" mkdir "%NUGET_PACKAGES%"
echo Restoring WinUI packages, please wait...
"%DOTNET%" restore "%~dp0spike-winui\Spike.csproj" -v:m
echo.
if errorlevel 1 (echo [FAILED] send the text above to the AI) else (echo [OK] packages ready)
pause
