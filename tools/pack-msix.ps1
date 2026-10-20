# Build + sign + install the WinUI 3 spike as MSIX.
# All packaging steps run inside %PUBLIC%\DeskNestPkg because the SDK tools
# (makeappx / signtool / makepri) are ANSI programs and mangle non-ASCII paths.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnet = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) ".dotnet-sdk\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES  = Join-Path $root ".nuget-winui"
$env:DOTNET_NOLOGO = "1"
$env:MSBUILDDISABLENODEREUSE = "1"

$pkgDir = Join-Path $env:PUBLIC "DeskNestPkg"
$layout = Join-Path $pkgDir "layout"
$msix   = Join-Path $pkgDir "DeskNestSpike.msix"
if (-not (Test-Path $pkgDir)) { New-Item -ItemType Directory -Path $pkgDir | Out-Null }

$proj  = Join-Path $root "spike-winui\Spike.csproj"
$props = @("-r","win-x64","-p:WindowsAppSDKSelfContained=true","-p:SelfContained=true",
           "-p:MicrosoftProjectPriGenerationEnable=false","-p:EnablePriGenTooling=false","-p:AppxGeneratePriEnabled=false")

Write-Host "[1/5] restore ..." -ForegroundColor Cyan
& $dotnet restore $proj @props -v:q
if ($LASTEXITCODE -ne 0) { throw "restore failed" }

Write-Host "[2/5] build ..." -ForegroundColor Cyan
& $dotnet build $proj -c Release --no-restore -m:1 -nr:false @props -v:q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$out = Join-Path $root "spike-winui\bin\Release\net10.0-windows10.0.19041.0\win-x64"
if (-not (Test-Path (Join-Path $out "Spike.exe"))) { throw "Spike.exe not found at $out" }

Write-Host "[3/5] staging into ASCII folder ..." -ForegroundColor Cyan
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Path $layout | Out-Null
Copy-Item (Join-Path $out "*") $layout -Recurse -Force
Copy-Item (Join-Path $root "_msix\AppxManifest.xml") $layout -Force
Copy-Item (Join-Path $root "_msix\Assets") $layout -Recurse -Force
Write-Host ("      layout: " + $layout) -ForegroundColor DarkGray

$sdk = Join-Path $root ".nuget-winui\microsoft.windows.sdk.buildtools\10.0.26100.4654\bin\10.0.26100.0\x64"
$makeappx = Join-Path $sdk "makeappx.exe"
$signtool = Join-Path $sdk "signtool.exe"

Write-Host "[4/5] pack ..." -ForegroundColor Cyan
if (Test-Path $msix) { Remove-Item $msix -Force }
$packOut = & $makeappx pack /d $layout /p $msix /o 2>&1
$packCode = $LASTEXITCODE
if ($packCode -ne 0) {
  Write-Host "makeappx said:" -ForegroundColor Yellow
  $packOut | ForEach-Object { Write-Host ("  " + $_) -ForegroundColor Yellow }
  throw "makeappx failed"
}
Write-Host ("      msix: " + [math]::Round((Get-Item $msix).Length/1MB,1) + " MB -> " + $msix) -ForegroundColor DarkGray
# Developer Mode on => unsigned install works.
$devKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
$devMode = $false
if (Test-Path $devKey) {
  $v = (Get-ItemProperty $devKey -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
  if ($v -eq 1) { $devMode = $true }
}

if ($devMode) {
  Write-Host "[5/5] developer mode -> install unsigned" -ForegroundColor Cyan
  $existing = Get-AppxPackage -Name "DeskNest.Spike"
  if ($existing) { Remove-AppxPackage $existing.PackageFullName }
  Add-AppxPackage -Path $msix -AllowUnsigned
} else {
  Write-Host "[5/5] signing ..." -ForegroundColor Cyan
  $subject = "CN=DeskNestDev"
  $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1
  if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
        -KeyUsage DigitalSignature -KeyExportPolicy Exportable `
        -FriendlyName "DeskNest Dev" -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
  }
  $cer = Join-Path $pkgDir "DeskNestDev.cer"
  Export-Certificate -Cert $cert -FilePath $cer | Out-Null
  $trusted = Join-Path "Cert:\CurrentUser\TrustedPeople" $cert.Thumbprint
  if (-not (Test-Path $trusted)) {
    Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
  }
  $signOut = & $signtool sign /fd SHA256 /a /sha1 $cert.Thumbprint $msix 2>&1
  if ($LASTEXITCODE -ne 0) {
    Write-Host "signtool said:" -ForegroundColor Yellow
    $signOut | ForEach-Object { Write-Host ("  " + $_) -ForegroundColor Yellow }
    throw "signing failed (turn developer mode on and rerun)"
  }
  $existing = Get-AppxPackage -Name "DeskNest.Spike"
  if ($existing) { Remove-AppxPackage $existing.PackageFullName }
  Add-AppxPackage -Path $msix
}

$pkg = Get-AppxPackage -Name "DeskNest.Spike"
if (-not $pkg) { throw "package not registered" }
Write-Host ("installed: " + $pkg.PackageFullName) -ForegroundColor Green
Start-Process ("shell:AppsFolder\$($pkg.PackageFamilyName)!App")
Write-Host "launched - a white window should appear now." -ForegroundColor Green