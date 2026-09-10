# 打发布包（方案 A：BepInEx 全家桶 + 插件 dll，整个拖进游戏根目录即生效）。
#
# 产物：dist\ChillDesktop-<版本>-BepInEx5-x64.zip
#   里面是 winhttp.dll / doorstop_config.ini / .doorstop_version / changelog.txt /
#   BepInEx\（core + plugins\ChillDesktop.Plugin.dll）/ README.md / LICENSE / THIRD-PARTY-NOTICES.md
#
# 注意：发布包**不含**任何游戏文件，也**不含** steam_appid.txt（那是本机开发调试用的）。
param(
    [string]$Version = "0.1.0",
    [string]$BepInExDir = (Join-Path $PSScriptRoot "..\dist\BepInEx_win_x64_5.4.23.5"),
    [string]$Root = (Join-Path $PSScriptRoot "..")
)

$ErrorActionPreference = "Stop"

$pluginDll = Join-Path $Root "ChillDesktop.Plugin\bin\Release\ChillDesktop.Plugin.dll"
if (-not (Test-Path $pluginDll)) {
    Write-Host "没找到 Release 版插件，先编译："
    Write-Host "  dotnet build -c Release ChillDesktop.Plugin\ChillDesktop.Plugin.csproj"
    throw "缺少 $pluginDll"
}
if (-not (Test-Path (Join-Path $BepInExDir "winhttp.dll"))) {
    throw "找不到 BepInEx 源目录：$BepInExDir"
}

$stage = Join-Path $Root "dist\stage"
$out   = Join-Path $Root "dist\ChillDesktop-$Version-BepInEx5-x64.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $out)   { Remove-Item $out -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# 1) BepInEx 全家桶
foreach ($item in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt", "BepInEx")) {
    $src = Join-Path $BepInExDir $item
    if (Test-Path $src) { Copy-Item $src (Join-Path $stage $item) -Recurse -Force }
}

# 2) 插件 dll
$plugins = Join-Path $stage "BepInEx\plugins"
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
Copy-Item $pluginDll $plugins -Force

# 3) 文档
foreach ($doc in @("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md")) {
    $p = Join-Path $Root $doc
    if (Test-Path $p) { Copy-Item $p $stage -Force }
}

# 4) 兜底：确保不带 pdb、不带开发用文件
Get-ChildItem $plugins -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force
foreach ($junk in @("steam_appid.txt", "chilldesktop.log", "LogOutput.log")) {
    $p = Join-Path $stage $junk
    if (Test-Path $p) { Remove-Item $p -Force }
}

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $out -Force
Remove-Item $stage -Recurse -Force

Write-Host "发布包已生成：$out"
Write-Host ("大小 {0:N0} KB" -f ((Get-Item $out).Length / 1KB))
