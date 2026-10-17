<#
  桌巢 DeskNest 一键编译（路径无关：脚本在哪个目录就用哪个目录）
  用法：
    .\build.ps1                # Debug 编译
    .\build.ps1 Release        # Release 编译
    .\build.ps1 Debug -Smoke   # 编完再跑冒烟（56 项）
#>
param(
    [string]$Config = "Debug",
    [switch]$Smoke
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# 这两个缓存目录不在项目里（很大，共用），换成别的机器要改这两行
$env:DOTNET_CLI_HOME = "C:\Users\李\Desktop\项目组\.dotnet-home"
$env:NUGET_PACKAGES  = "C:\Users\李\Desktop\项目组\.nuget"
$dn = "C:\Users\李\.dotnet-sdk\dotnet.exe"

if (-not (Test-Path $dn)) { throw "找不到 dotnet：$dn" }

Push-Location $root
try {
    Write-Host "== 编译 $Config ==" -ForegroundColor Cyan
    & $dn build DeskNest.slnx -c $Config
    if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }

    if ($Smoke) {
        Write-Host "== 冒烟测试 ==" -ForegroundColor Cyan
        & $dn build "tools\DeskNest.Smoke\DeskNest.Smoke.csproj" -c $Config | Out-Null
        & $dn exec "tools\DeskNest.Smoke\bin\$Config\net10.0-windows\DeskNest.Smoke.dll"
        if ($LASTEXITCODE -ne 0) { throw "冒烟失败（退出码 $LASTEXITCODE）" }
    }

    Write-Host "== 完成 ==" -ForegroundColor Green
    Write-Host "运行：.\run.ps1 $Config"
}
finally { Pop-Location }
