# Register the WinUI shell app (developer mode, no signature needed).
# Fixed install dir + overwrite update: no more DeskNestShellLayout_HHMMSS garbage.
# NOTE: keep this file ASCII-only - powershell.exe -File reads .ps1 as ANSI on this machine.
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Expand-Archive would otherwise spam 1000s of lines
$here = Split-Path -Parent $PSScriptRoot
$msix = Join-Path $here "dist\winui\DeskNestShell-unsigned.msix"
if (-not (Test-Path $msix)) { throw "Missing $msix - run build-shell.cmd first." }

# Developer mode check (prerequisite for unsigned MSIX loose registration)
$devKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
$devMode = $false
if (Test-Path $devKey) {
  $v = (Get-ItemProperty $devKey -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
  if ($v -eq 1) { $devMode = $true }
}
if (-not $devMode) {
  Write-Host "Developer mode is OFF." -ForegroundColor Yellow
  Write-Host "Opening Settings > System > For developers. Turn it ON, then run this again." -ForegroundColor Yellow
  Start-Process "ms-settings:developers"
  exit 1
}

# Unregister old package first (same-name overwrite requires it)
$old = Get-AppxPackage -Name "DeskNest.Shell" -ErrorAction SilentlyContinue
if ($old) {
  Remove-AppxPackage $old.PackageFullName
  Write-Host "Old version unregistered." -ForegroundColor DarkGray
}

# Fixed active dir. Move the previous one aside with a unique name (never bulk-delete:
# deleting hundreds of files trips the machine's bulk-delete guard). Old copies are kept,
# user can remove them manually. Expand-Archive into an empty dir is reliable.
$layout = Join-Path $env:LOCALAPPDATA "DeskNest\AppPackage"
if (Test-Path $layout) {
  $stale = Join-Path $env:LOCALAPPDATA ("DeskNest\AppPackage.prev-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
  try {
    Move-Item $layout $stale -Force -ErrorAction Stop
    Write-Host ("Previous install kept at: " + $stale) -ForegroundColor DarkGray
  } catch { throw ("Could not move old install aside: " + $_.Exception.Message) }
}
New-Item -ItemType Directory -Path $layout -Force | Out-Null

$zip = Join-Path $env:TEMP "desknest-shell-pkg.zip"
Copy-Item $msix $zip -Force
Expand-Archive -Path $zip -DestinationPath $layout -Force
try { Remove-Item $zip -Force -ErrorAction SilentlyContinue } catch { }

Add-AppxPackage -Register (Join-Path $layout "AppxManifest.xml")

$pkg = Get-AppxPackage -Name "DeskNest.Shell"
if (-not $pkg) { throw "Registration failed." }

Write-Host ("Installed: " + $pkg.PackageFullName) -ForegroundColor Green
Write-Host ("Location: " + $layout) -ForegroundColor DarkGray
Start-Process ("shell:AppsFolder\$($pkg.PackageFamilyName)!App")
Write-Host "Launched. Find 'DeskNest' in Start menu next time." -ForegroundColor Green
