@echo off
setlocal enabledelayedexpansion
pushd "%~dp0"
set "DOTNET=..\..\.dotnet-sdk\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
set "DOTNET_CLI_HOME=%~dp0.dotnet-home"
set "NUGET_PACKAGES=%~dp0.nuget-winui"
set "PROPS=-r win-x64 -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -p:MicrosoftProjectPriGenerationEnable=false -p:EnablePriGenTooling=false -p:AppxGeneratePriEnabled=false"
echo [1/3] restore ...
"%DOTNET%" restore "%~dp0spike-winui\Spike.csproj" -r win-x64 -v:q
if errorlevel 1 (echo [FAILED] restore & pause & popd & exit /b 1)
echo [2/3] build ...
"%DOTNET%" build "%~dp0spike-winui\Spike.csproj" -c Debug --no-restore -m:1 -nr:false %PROPS% -v:q
if errorlevel 1 (echo [FAILED] build & pause & popd & exit /b 1)
call "%~dp0run-spike-now.cmd"
popd
pause
