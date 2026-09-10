# 卸载本机游戏目录里的 BepInEx，让游戏完全恢复纯净。
# 只删除 BepInEx 自身的文件（winhttp.dll / doorstop_config.ini / .doorstop_version /
# changelog.txt / BepInEx\），绝不触碰游戏原始文件。
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $GameDir)) { throw "游戏目录不存在：$GameDir" }

$running = Get-Process -Name "Chill With You" -ErrorAction SilentlyContinue
if ($running) { throw "游戏正在运行，请先关闭游戏再卸载。" }

$items = @("winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt", "BepInEx")
$removed = @()

foreach ($item in $items) {
    $p = Join-Path $GameDir $item
    if (Test-Path $p) { Remove-Item $p -Recurse -Force; $removed += $item }
}

if ($removed.Count -eq 0) {
    Write-Host "没有发现 BepInEx 文件，游戏本来就是纯净的。"
} else {
    Write-Host ("已删除：" + ($removed -join ", "))
    Write-Host "游戏已恢复纯净 vanilla 状态。"
}
