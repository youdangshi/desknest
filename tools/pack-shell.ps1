# Build + pack the WinUI 3 shell (DeskNest.WinUI) into dist\winui\DeskNestShell-unsigned.msix
#
# Two environment traps handled here (both cost hours to find):
#   1. MakePri.exe is an ANSI tool: it mangles non-ASCII paths. The project lives under
#      C:\Users\<chinese>\... so we build through an ASCII junction (C:\DNB).
#   2. MakePri also writes to %TEMP%. If TEMP points outside the project tree it dies with
#      0x80070005 (exit 5 = access denied) and prints no error at all, so keep TEMP under
#      the ASCII junction too.
#
# Usage:  .\tools\pack-shell.ps1 [-Install]      (-Install also registers + launches)
param([switch]$Install)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$dotnet = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) ".dotnet-sdk\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

# --- ASCII work dirs -------------------------------------------------------
$junction = "C:\DNB"
$needJunction = $true
if (Test-Path $junction) {
  $t = (Get-Item $junction).Target
  if ($t -and ($t.TrimEnd('\') -eq $root.TrimEnd('\'))) { $needJunction = $false }
  else { Remove-Item $junction -Recurse -Force }
}
if ($needJunction) { New-Item -ItemType Junction -Path $junction -Target $root | Out-Null }
$buildRoot = $junction

# MakePri needs an ASCII %TEMP% that is also inside the project tree (outside = access denied).
$asciiTemp = Join-Path $junction "_tmp"
if (-not (Test-Path $asciiTemp)) { New-Item -ItemType Directory -Path $asciiTemp -Force | Out-Null }
$env:TEMP = $asciiTemp
$env:TMP  = $asciiTemp

$pkgDir = Join-Path $env:PUBLIC "DeskNestPkg"
$layout = Join-Path $pkgDir "shell"
if (-not (Test-Path $pkgDir)) { New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null }

$env:DOTNET_CLI_HOME = Join-Path $buildRoot ".dotnet-home"
$env:NUGET_PACKAGES  = Join-Path $buildRoot ".nuget-winui"
$env:DOTNET_NOLOGO   = "1"
$env:MSBUILDDISABLENODEREUSE = "1"

$proj = Join-Path $buildRoot "src\DeskNest.WinUI\DeskNest.WinUI.csproj"
Write-Host "[1/4] build (via ASCII junction $buildRoot) ..." -ForegroundColor Cyan
& $dotnet build $proj -c Release -r win-x64 -m:1 -nr:false -v:m
if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

$out = Join-Path $buildRoot "src\DeskNest.WinUI\bin\Release\net10.0-windows10.0.19041.0\win-x64"
if (-not (Test-Path (Join-Path $out "DeskNest.WinUI.exe"))) { throw "DeskNest.WinUI.exe missing at $out" }
$pri = Join-Path $out "DeskNest.WinUI.pri"
if (-not (Test-Path $pri)) { throw "DeskNest.WinUI.pri missing - PRI generation is off? (see csproj)" }

Write-Host "[2/4] staging into ASCII folder ..." -ForegroundColor Cyan
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Path $layout | Out-Null
Copy-Item (Join-Path $out "*") $layout -Recurse -Force
Copy-Item (Join-Path $root "_msix\AppxManifest.Shell.xml") (Join-Path $layout "AppxManifest.xml") -Force
Copy-Item (Join-Path $root "_msix\Assets") $layout -Recurse -Force
# MRT looks for resources.pri at the package root; the build names it after the assembly.
Copy-Item $pri (Join-Path $layout "resources.pri") -Force
Write-Host ("      layout: " + $layout) -ForegroundColor DarkGray

Write-Host "[3/4] makeappx ..." -ForegroundColor Cyan
$sdk = Join-Path $root ".nuget-winui\microsoft.windows.sdk.buildtools\10.0.26100.4654\bin\10.0.26100.0\x64"
$makeappx = Join-Path $sdk "makeappx.exe"
$dest = Join-Path $root "dist\winui"
if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
$msix = Join-Path $dest "DeskNestShell-unsigned.msix"
if (Test-Path $msix) { Remove-Item $msix -Force }
$packOut = & $makeappx pack /d $layout /p $msix /o 2>&1
if ($LASTEXITCODE -ne 0) {
  $packOut | ForEach-Object { Write-Host ("  " + $_) -ForegroundColor Yellow }
  throw "makeappx failed"
}
Write-Host ("      msix: " + [math]::Round((Get-Item $msix).Length / 1MB, 1) + " MB -> " + $msix) -ForegroundColor DarkGray

if ($Install) {
  Write-Host "[4/4] register + launch ..." -ForegroundColor Cyan
  & (Join-Path $PSScriptRoot "install-shell.ps1")
  if ($LASTEXITCODE -ne 0) { throw "install failed" }
} else {
  Write-Host "[4/4] done. run .\install-desknest-shell.cmd to register + launch" -ForegroundColor Cyan
}
