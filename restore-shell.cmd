@echo off
setlocal
pushd "%~dp0"
set "DOTNET=..\..\.dotnet-sdk\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
set "DOTNET_CLI_HOME=%~dp0.dotnet-home"
set "NUGET_PACKAGES=%~dp0.nuget-winui"
set "DOTNET_NOLOGO=1"
echo Restoring the WinUI shell project (one time, needs network) ...
"%DOTNET%" restore "%~dp0src\DeskNest.WinUI\DeskNest.WinUI.csproj" -r win-x64 -v:m
echo.
if errorlevel 1 (echo [FAILED] paste the text above to the AI) else (echo [OK] restore finished - tell the AI)
popd
pause
