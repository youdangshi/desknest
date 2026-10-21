# Launch the already-registered WinUI shell. No rebuild, no 87 MB re-extract.
# If it says "not registered", run .\build-shell.cmd once.
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$pkg = Get-AppxPackage -Name "DeskNest.Shell"
if (-not $pkg) { throw "DeskNest.Shell is not registered - run .\build-shell.cmd once (or install-desknest-shell.cmd)." }

Start-Process ("shell:AppsFolder\$($pkg.PackageFamilyName)!App")
Write-Host ("launched " + $pkg.PackageFullName) -ForegroundColor Green
