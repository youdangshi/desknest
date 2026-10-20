# Install the WinUI 3 spike package.
# Path 1 (developer mode ON): register the app from its loose files - no certificate needed.
# Path 2 (OFF): trust our cert in LocalMachine\TrustedPeople (UAC) then install the signed package.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $PSScriptRoot
$spike = Join-Path $here "dist\spike"
$signedMsix   = Join-Path $spike "DeskNestSpike.msix"
$unsignedMsix = Join-Path $spike "DeskNestSpike-unsigned.msix"
$cer = Join-Path $spike "DeskNestDev.cer"

$devKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
$devMode = $false
if (Test-Path $devKey) {
  $v = (Get-ItemProperty $devKey -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
  if ($v -eq 1) { $devMode = $true }
}

function Remove-Old {
  $e = Get-AppxPackage -Name "DeskNest.Spike"
  if ($e) { Remove-AppxPackage $e.PackageFullName }
}

if ($devMode) {
  Write-Host "developer mode: ON -> registering unpacked app (no signature needed)" -ForegroundColor Cyan
  if (-not (Test-Path $unsignedMsix)) { throw "missing $unsignedMsix" }
  # ????????? app ???????????????????
  Remove-Old
  $layout = Join-Path $env:LOCALAPPDATA ("DeskNestSpikeLayout_" + (Get-Date -Format "HHmmss"))
  New-Item -ItemType Directory -Path $layout -Force | Out-Null
  # ????????????????????????
  Get-ChildItem $env:LOCALAPPDATA -Directory -Filter "DeskNestSpikeLayout*" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $layout } |
    ForEach-Object { try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop } catch { } }
  $zip = Join-Path $layout "pkg.zip"
  Copy-Item $unsignedMsix $zip -Force
  Expand-Archive -Path $zip -DestinationPath $layout -Force
  Remove-Item $zip -Force
  $manifest = Join-Path $layout "AppxManifest.xml"
  if (-not (Test-Path $manifest)) { throw "AppxManifest.xml not found after expand" }
  Add-AppxPackage -Register $manifest
} else {
  Write-Host "developer mode: OFF -> trusting the cert needs admin (UAC prompt follows)" -ForegroundColor Cyan
  if (-not (Test-Path $cer)) { throw "missing $cer" }
  $script = @"
`$c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 '$cer'
`$s = New-Object System.Security.Cryptography.X509Certificates.X509Store 'TrustedPeople','LocalMachine'
`$s.Open('ReadWrite')
if (-not (`$s.Certificates | Where-Object { `$_.Thumbprint -eq `$c.Thumbprint })) { `$s.Add(`$c) }
`$s.Close()
"@
  $tmp = Join-Path $env:TEMP "trust-desknest-cert.ps1"
  Set-Content -Path $tmp -Value $script -Encoding ASCII
  $p = Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$tmp`"" -Verb RunAs -Wait -PassThru
  if ($p.ExitCode -ne 0) { throw "certificate trust failed (UAC cancelled?)" }
  Remove-Old
  Add-AppxPackage -Path $signedMsix
}

$pkg = Get-AppxPackage -Name "DeskNest.Spike"
if (-not $pkg) { throw "package not registered" }
Write-Host ("installed: " + $pkg.PackageFullName) -ForegroundColor Green
Start-Process ("shell:AppsFolder\$($pkg.PackageFamilyName)!App")
Write-Host "launched - a white window should appear now." -ForegroundColor Green