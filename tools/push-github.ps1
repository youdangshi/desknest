# 把本地仓库推送到 GitHub（本机直连 github.com 不通，必须走代理）
#
# 用法：
#   pwsh -File tools\push-github.ps1 -Token ghp_xxx -Owner 你的用户名 -Repo desknest
# 说明：
#   - 第一次运行会用 API 创建仓库（如果已存在就跳过），然后推送 main 与所有 tag
#   - 代理默认 127.0.0.1:7897（Clash Verge），可用 -Proxy 改
#   - Token 需要 repo 权限（classic）或 Contents+Administration 权限（fine-grained）

param(
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$Owner,
    [string]$Repo = "desknest",
    [string]$Proxy = "http://127.0.0.1:7897",
    [switch]$Private
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not (Test-Path ".git")) { throw "当前目录不是 git 仓库：$root" }

Write-Host "1/4 检查 GitHub 连通性（经 $Proxy）…"
$probe = curl.exe -sS --max-time 20 -x $Proxy -o NUL -w "%{http_code}" https://api.github.com/rate_limit
if ($probe -ne "200") { throw "连不上 api.github.com（HTTP $probe）。请确认代理在运行。" }
Write-Host "    OK"

Write-Host "2/4 创建仓库 $Owner/$Repo（已存在则跳过）…"
$body = @{ name = $Repo; private = [bool]$Private; description = "桌巢 DeskNest —— Windows 桌面整理 + 桌面组件 + 桌宠 + 本地 ComfyUI 出图（MIT）" } | ConvertTo-Json
$tmp = Join-Path $env:TEMP "desknest-create-repo.json"
Set-Content -Path $tmp -Value $body -Encoding UTF8
$code = curl.exe -sS -x $Proxy -o (Join-Path $env:TEMP "desknest-repo-resp.json") -w "%{http_code}" `
    -X POST -H "Authorization: Bearer $Token" -H "Accept: application/vnd.github+json" `
    -H "Content-Type: application/json" --data-binary "@$tmp" https://api.github.com/user/repos
if ($code -eq "201") { Write-Host "    已创建" }
elseif ($code -eq "422") { Write-Host "    仓库已存在，继续推送" }
else { Write-Host "    创建返回 HTTP $code（若是权限问题，也可以先去网页手动建空仓库）" }

Write-Host "3/4 配置远端并推送 main…"
$auth = "https://$Owner`:$Token@github.com/$Owner/$Repo.git"
git remote remove origin 2>$null
git remote add origin $auth
git -c http.proxy=$Proxy -c https.proxy=$Proxy push -u origin main

Write-Host "4/4 推送标签…"
git -c http.proxy=$Proxy -c https.proxy=$Proxy push origin --tags

Write-Host ""
Write-Host "完成：https://github.com/$Owner/$Repo"
Write-Host "提示：远端地址里带了 token，建议改成不含 token 的形式："
Write-Host "      git remote set-url origin https://github.com/$Owner/$Repo.git"
