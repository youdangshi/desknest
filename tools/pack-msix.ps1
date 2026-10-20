# Build + sign + install the WinUI 3 spike as an MSIX package.
# Runs as the signed-in user (no admin needed).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnet = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) ".dotnet-sdk\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES  = Join-Path $root ".nuget-winui"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:MSBUILDDISABLENODEREUSE = "1"

$proj = Join-Path $root "spike-winui\Spike.csproj"
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

Write-Host "[3/5] staging package layout ..." -ForegroundColor Cyan
$layout = Join-Path $root "_msix\layout"
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Path $layout | Out-Null
Copy-Item (Join-Path $out "*") $layout -Recurse -Force
Copy-Item (Join-Path $root "_msix\AppxManifest.xml") $layout -Force
Copy-Item (Join-Path $root "_msix\Assets") $layout -Recurse -Force

$sdk = Join-Path $root ".nuget-winui\microsoft.windows.sdk.buildtools\10.0.26100.4654\bin\10.0.26100.0\x64"
$makeappx = Join-Path $sdk "makeappx.exe"
$signtool = Join-Path $sdk "signtool.exe"

Write-Host "[4/5] pack + sign ..." -ForegroundColor Cyan
$msix = Join-Path $root "_msix\DeskNestSpike.msix"
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx pack /d $layout /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

$subject = "CN=DeskNestDev"
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1
if (-not $cert) {
  $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
      -KeyUsage DigitalSignature -FriendlyName "DeskNest Dev" `
      -CertStoreLocation "Cert:\CurrentUser\My" `
      -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
}
$pfx = Join-Path $root "_msix\DeskNestDev.pfx"
$cer = Join-Path $root "_msix\DeskNestDev.cer"
$pwd = ConvertTo-SecureString -String "desknest" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

$trusted = Join-Path "Cert:\CurrentUser\TrustedPeople" $cert.Thumbprint
if (-not (Test-Path $trusted)) {
  Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
}

& $signtool sign /fd SHA256 /a /f $pfx /p "desknest" $msix | Out-Null
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }

Write-Host "[5/5] install ..." -ForegroundColor Cyan
$existing = Get-AppxPackage -Name "DeskNest.Spike"
if ($existing) { Remove-AppxPackage $existing.PackageFullName }
Add-AppxPackage -Path $msix
$pkg = Get-AppxPackage -Name "DeskNest.Spike"
Write-Host ("installed: " + $pkg.PackageFullName) -ForegroundColor Green

$targets = @("shell:AppsFolder\$($pkg.PackageFamilyName)!App")
foreach ($t in $targets) { Start-Process $t }
Write-Host "launched. look for the window." -ForegroundColor Green
Write-Host "IMPORTANT: with an MSIX package the app runs with normal permissions." -ForegroundColor Yellow