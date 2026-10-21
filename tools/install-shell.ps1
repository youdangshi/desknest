# Register the WinUI shell app (developer mode, no signature needed).
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $PSScriptRoot
$msix = Join-Path $here "dist\winui\DeskNestShell-unsigned.msix"
if (-not (Test-Path $msix)) { throw "missing $msix" }

$devKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
$devMode = $false
if (Test-Path $devKey) {
  $v = (Get-ItemProperty $devKey -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
  if ($v -eq 1) { $devMode = $true }
}
if (-not $devMode) {
  Write-Host "developer mode is OFF." -ForegroundColor Yellow
  Write-Host "opening Settings > System > For developers ..." -ForegroundColor Yellow
  Start-Process "ms-settings:developers"
  Write-Host "turn ON 'Developer Mode', then run this script again." -ForegroundColor Yellow
  exit 1
}

$old = Get-AppxPackage -Name "DeskNest.Shell"
if ($old) { Remove-AppxPackage $old.PackageFullName }

$layout = Join-Path $env:LOCALAPPDATA ("DeskNestShellLayout_" + (Get-Date -Format "HHmmss"))
New-Item -ItemType Directory -Path $layout -Force | Out-Null
Get-ChildItem $env:LOCALAPPDATA -Directory -Filter "DeskNestShellLayout*" -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -ne $layout } | ForEach-Object { try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop } catch { } }

$zip = Join-Path $layout "pkg.zip"
Copy-Item $msix $zip -Force
Expand-Archive -Path $zip -DestinationPath $layout -Force
Remove-Item $zip -Force
Add-AppxPackage -Register (Join-Path $layout "AppxManifest.xml")

$pkg = Get-AppxPackage -Name "DeskNest.Shell"
if (-not $pkg) { throw "registration failed" }
Write-Host ("installed: " + $pkg.PackageFullName) -ForegroundColor Green
Start-Process ("shell:AppsFolder\$($pkg.PackageFamilyName)!App")
Write-Host "launched - the DeskNest window should appear." -ForegroundColor Green