<#
  桌巢 DeskNest 启动脚本
  用法：
    .\run.ps1            # 运行 Debug 构建
    .\run.ps1 Release    # 运行 Release 构建
  说明：如果编译报"文件被占用"，先把正在运行的桌巢退出（托盘右键 → 退出）。
#>
param([string]$Config = "Debug")

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dn = "C:\Users\李\.dotnet-sdk\dotnet.exe"
$dll = Join-Path $root "src\DeskNest.App\bin\$Config\net10.0-windows\DeskNest.dll"

if (-not (Test-Path $dll)) {
    throw "还没编译（找不到 $dll）。先跑 .\build.ps1 $Config"
}

Start-Process -FilePath $dn -ArgumentList @("exec", $dll) -WorkingDirectory $root -WindowStyle Hidden
Write-Host "已启动桌巢（$Config）。托盘图标右键可以退出。" -ForegroundColor Green
