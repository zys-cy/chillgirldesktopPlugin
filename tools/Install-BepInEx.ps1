# 把 BepInEx 5（x64）部署进游戏根目录。
# 用途：开发期本机安装 + 发布包制作前的验证。
# 卸载见 Uninstall-BepInEx.ps1 —— 删掉这几项游戏即完全恢复纯净，不改任何游戏原始文件。
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story",
    [string]$SourceDir = (Join-Path $PSScriptRoot "..\dist\BepInEx_win_x64_5.4.23.5")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $GameDir)) { throw "游戏目录不存在：$GameDir" }
if (-not (Test-Path (Join-Path $SourceDir "winhttp.dll"))) { throw "找不到 BepInEx 源文件：$SourceDir" }

$running = Get-Process -Name "Chill With You" -ErrorAction SilentlyContinue
if ($running) { throw "游戏正在运行，请先关闭游戏再安装。" }

# 顶层散装文件 + BepInEx 目录
$items = @("winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt", "BepInEx")

foreach ($item in $items) {
    $src = Join-Path $SourceDir $item
    if (-not (Test-Path $src)) { continue }
    $dst = Join-Path $GameDir $item
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    if ((Get-Item $src).PSIsContainer) {
        Copy-Item $src $dst -Recurse -Force
    } else {
        Copy-Item $src $dst -Force
    }
    Write-Host "已部署 $item"
}

# 插件目录（BepInEx 首次运行也会自建，这里先建好便于直接放 dll）
foreach ($sub in @("plugins", "config")) {
    $p = Join-Path (Join-Path $GameDir "BepInEx") $sub
    if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
}

Write-Host ""
Write-Host "BepInEx 5 已安装到：$GameDir"
Write-Host "卸载：tools\Uninstall-BepInEx.ps1"
