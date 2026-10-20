# Import our dev certificate (current user) and install the spike package.
$here = Split-Path -Parent $PSScriptRoot
$cer  = Join-Path $here "dist\spike\DeskNestDev.cer"
$msix = Join-Path $here "dist\spike\DeskNestSpike.msix"
if (-not (Test-Path $msix)) { throw "msix not found: $msix" }

$c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $cer
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store "TrustedPeople", "CurrentUser"
$store.Open("ReadWrite")
if (-not ($store.Certificates | Where-Object { $_.Thumbprint -eq $c.Thumbprint })) {
  $store.Add($c)
  Write-Host ("certificate trusted: " + $c.Thumbprint) -ForegroundColor Green
}
$store.Close()

$existing = Get-AppxPackage -Name "DeskNest.Spike"
if ($existing) { Remove-AppxPackage $existing.PackageFullName }

Add-AppxPackage -Path $msix
$pkg = Get-AppxPackage -Name "DeskNest.Spike"
if (-not $pkg) { throw "install failed" }
Write-Host ("installed: " + $pkg.PackageFullName) -ForegroundColor Green
Start-Process ("shell:AppsFolder\$($pkg.PackageFamilyName)!App")
Write-Host "launched - a white window should appear." -ForegroundColor Green