# 客观验证「游戏确实铺在桌面上、Win+D 之后也还在」。
#
# 原理：把「屏幕截图」和「游戏窗口自己的 PrintWindow 抓帧」各自缩到 64x40 后逐像素比对。
#   如果游戏就是当前可见的壁纸，两张图应该高度一致（平均像素差很小）；
#   如果游戏被最小化/被别的东西盖住，屏幕上看不到它，差异会非常大。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File poc\verify_wallpaper.ps1            # 只比对当前屏幕
#   powershell -ExecutionPolicy Bypass -File poc\verify_wallpaper.ps1 -PressWinD # 先按 Win+D 再比对
param(
    [switch]$PressWinD,
    [string]$OutDir = "$PSScriptRoot\..\dist"
)

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Vw -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
[DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
[DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
[DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr h);
[DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr h, int w, int hh);
[DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr h, IntPtr o);
[DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr h);
[DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
public delegate bool EnumProc(IntPtr h, IntPtr l);
'@

function Get-GameWindow {
    $script:found = [IntPtr]::Zero
    $script:area = -1
    $cb = [Vw.Win+EnumProc]{
        param($h, $l)
        $procId = 0
        [Vw.Win]::GetWindowThreadProcessId($h, [ref]$procId) | Out-Null
        $pn = "?"
        try { $pn = (Get-Process -Id $procId -ErrorAction Stop).ProcessName } catch { }
        if ($pn -like "Chill With You*") {
            $c = New-Object System.Text.StringBuilder 256
            [Vw.Win]::GetClassName($h, $c, 256) | Out-Null
            if ($c.ToString() -eq "UnityWndClass") {
                $r = New-Object Vw.Win+RECT
                [Vw.Win]::GetWindowRect($h, [ref]$r) | Out-Null
                $a = ($r.Right - $r.Left) * ($r.Bottom - $r.Top)
                if ($a -gt $script:area) { $script:area = $a; $script:found = $h }
            }
        }
        return $true
    }
    [Vw.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:found
}

function Resize-ToGrid([System.Drawing.Image]$img, [int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img, 0, 0, $w, $h)
    $g.Dispose()
    return $bmp
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$hwnd = Get-GameWindow
if ($hwnd -eq [IntPtr]::Zero) { Write-Host "没找到游戏窗口，先启动游戏。"; exit 1 }
$r = New-Object Vw.Win+RECT
[Vw.Win]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$gw = $r.Right - $r.Left; $gh = $r.Bottom - $r.Top
Write-Host ("游戏窗口 0x{0:X}  位置=({1},{2})  尺寸={3}x{4}" -f $hwnd.ToInt64(), $r.Left, $r.Top, $gw, $gh)

if ($PressWinD) {
    Write-Host ">>> 按 Win+D"
    [Vw.Win]::keybd_event(0x5B, 0, 0, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [Vw.Win]::keybd_event(0x44, 0, 0, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [Vw.Win]::keybd_event(0x44, 0, 2, [System.UIntPtr]::Zero)
    [Vw.Win]::keybd_event(0x5B, 0, 2, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1200
}

# 1) PrintWindow 抓游戏自己的画面（PW_RENDERFULLCONTENT=2，不依赖前台/遮挡）
$gameBmp = New-Object System.Drawing.Bitmap($gw, $gh)
$gh2 = [System.Drawing.Graphics]::FromImage($gameBmp)
$hdc1 = $gh2.GetHdc()
$ok = [Vw.Win]::PrintWindow($hwnd, $hdc1, 2)
$gh2.ReleaseHdc($hdc1)
$gh2.Dispose()
Write-Host "PrintWindow 结果: $ok"

# 2) 抓屏幕
$sw = [Vw.Win]::GetSystemMetrics(0); $sh = [Vw.Win]::GetSystemMetrics(1)
$dc = [Vw.Win]::GetDC([IntPtr]::Zero)
$mem = [Vw.Win]::CreateCompatibleDC($dc)
$sbmp = [Vw.Win]::CreateCompatibleBitmap($dc, $sw, $sh)
[Vw.Win]::SelectObject($mem, $sbmp) | Out-Null
[Vw.Win]::BitBlt($mem, 0, 0, $sw, $sh, $dc, 0, 0, 0x00CC0020) | Out-Null
$screenBmp = [System.Drawing.Image]::FromHbitmap($sbmp)
[Vw.Win]::DeleteObject($sbmp); [Vw.Win]::DeleteDC($mem); [Vw.Win]::ReleaseDC([IntPtr]::Zero, $dc)

$gameBmp.Save((Join-Path $OutDir "verify_game.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$screenBmp.Save((Join-Path $OutDir "verify_screen.png"), [System.Drawing.Imaging.ImageFormat]::Png)

# 3) 各缩到 64x40 逐像素比对
$gA = Resize-ToGrid $gameBmp 64 40
$gB = Resize-ToGrid $screenBmp 64 40
$sum = 0.0; $n = 0; $worst = 0
for ($y = 0; $y -lt 40; $y++) {
    for ($x = 0; $x -lt 64; $x++) {
        $c1 = $gA.GetPixel($x, $y); $c2 = $gB.GetPixel($x, $y)
        $d = ([math]::Abs($c1.R - $c2.R) + [math]::Abs($c1.G - $c2.G) + [math]::Abs($c1.B - $c2.B)) / 3.0
        $sum += $d; $n++
        if ($d -gt $worst) { $worst = $d }
    }
}
$mae = $sum / $n
Write-Host ("全屏平均像素差 MAE = {0:N2}  (最大 {1:N1})" -f $mae, $worst)

# 桌面图标集中在左上角，单独算一块区域的差异（用来判断图标有没有透出来）
$iconSum = 0.0; $iconN = 0
for ($y = 0; $y -lt 20; $y++) {
    for ($x = 0; $x -lt 16; $x++) {
        $c1 = $gA.GetPixel($x, $y); $c2 = $gB.GetPixel($x, $y)
        $iconSum += ([math]::Abs($c1.R - $c2.R) + [math]::Abs($c1.G - $c2.G) + [math]::Abs($c1.B - $c2.B)) / 3.0
        $iconN++
    }
}
Write-Host ("左上角（桌面图标区）MAE = {0:N2}" -f ($iconSum / $iconN))

if ($mae -lt 12) { Write-Host "结论：屏幕上看到的基本就是游戏画面 —— 壁纸可见 ✔" }
elseif ($mae -lt 30) { Write-Host "结论：屏幕与游戏画面部分相似（可能有窗口压在游戏上，或正在切场景）" }
else { Write-Host "结论：屏幕上看不到游戏画面 —— 壁纸不可见 ✘" }
