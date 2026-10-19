@echo off
echo Preparing NuGet packages for WinUI 3 (first run may take a few minutes)...
set DOTNET_CLI_HOME=C:\Users\李\Desktop\DeskNest\.dotnet-home
set NUGET_PACKAGES=C:\Users\李\Desktop\DeskNest\.nuget-winui
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
"C:\Users\李\.dotnet-sdk\dotnet.exe" restore "C:\Users\李\Desktop\DeskNest\spike-winui\Spike.csproj" -v:m
echo.
if %ERRORLEVEL%==0 (echo [OK] packages ready) else (echo [FAILED] send the output above to the AI)
pause