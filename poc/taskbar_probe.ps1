# 任务栏隐藏/恢复的客观取证：只抓屏幕底部那一条（任务栏所在区域）并算哈希。
# 同一个状态多次抓应该得到相同哈希；不同状态应该不同。
#
# 用法（每步单独跑一条，避免时序竞态）：
#   powershell -File poc\taskbar_probe.ps1 -Tag A          # 抓当前底部条
#   powershell -File poc\taskbar_probe.ps1 -PressF11 -Tag B
#   powershell -File poc\taskbar_probe.ps1 -PressF11 -Tag C
#   powershell -File poc\taskbar_probe.ps1 -WinD            # 先 Win+D 露出桌面
param(
    [switch]$PressF11,
    [switch]$WinD,
    [string]$Tag = "x",
    [int]$StripHeight = 48,
    [string]$OutDir = "$PSScriptRoot\..\dist"
)

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Tp -Name W -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
[DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
[DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d,int x,int y,int w,int h,IntPtr s,int sx,int sy,int rop);
[DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr h);
[DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr h,int w,int hh);
[DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr h, IntPtr o);
[DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr h);
[DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
'@

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$SW = [Tp.W]::GetSystemMetrics(0)
$SH = [Tp.W]::GetSystemMetrics(1)

function GrabStrip {
    $y = $SH - $StripHeight
    $dc  = [Tp.W]::GetDC([IntPtr]::Zero)
    $mem = [Tp.W]::CreateCompatibleDC($dc)
    $bmp = [Tp.W]::CreateCompatibleBitmap($dc, $SW, $StripHeight)
    [void][Tp.W]::SelectObject($mem, $bmp)
    [void][Tp.W]::BitBlt($mem, 0, 0, $SW, $StripHeight, $dc, 0, $y, 0x00CC0020)
    $img = [System.Drawing.Image]::FromHbitmap($bmp)
    [void][Tp.W]::DeleteObject($bmp)
    [void][Tp.W]::DeleteDC($mem)
    [void][Tp.W]::ReleaseDC([IntPtr]::Zero, $dc)
    return $img
}

function HashOf([System.Drawing.Image]$img) {
    # 用 PNG 字节流做哈希（同一画面编码结果确定）
    $ms = New-Object System.IO.MemoryStream
    $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').Substring(0, 16)
}

function PressF11Key {
    [void][Tp.W]::keybd_event(0x7A, 0, 0, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [void][Tp.W]::keybd_event(0x7A, 0, 2, [System.UIntPtr]::Zero)
}

function PressWinD {
    [void][Tp.W]::keybd_event(0x5B, 0, 0, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    [void][Tp.W]::keybd_event(0x44, 0, 0, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    [void][Tp.W]::keybd_event(0x44, 0, 2, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    [void][Tp.W]::keybd_event(0x5B, 0, 2, [System.UIntPtr]::Zero)
}

if ($WinD) {
    PressWinD
    Start-Sleep -Seconds 2
    Write-Host "已按 Win+D 露出桌面"
}

if ($PressF11) {
    PressF11Key
    Start-Sleep -Seconds 2
    Write-Host "已按 F11"
}

$tray = [Tp.W]::FindWindow("Shell_TrayWnd", $null)
$img = GrabStrip
$hash = HashOf $img
$png = Join-Path $OutDir "tb_$Tag.png"
$img.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Host ("[{0}] 底部条 {1}x{2}  哈希={3}  任务栏 IsWindowVisible={4}  图={5}" -f `
    $Tag, $SW, $StripHeight, $hash, [Tp.W]::IsWindowVisible($tray), $png)
Write-Host "HASH=$Tag=$hash"
