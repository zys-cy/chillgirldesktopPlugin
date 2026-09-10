# 判断「任务栏此刻到底看不看得见」——最直接的判据：
#   在任务栏所在位置取 WindowFromPoint，看最上面的是不是 Shell_TrayWnd。
#   WindowFromPoint 会跳过隐藏窗口且遵守 Z 序，所以它返回 Shell_TrayWnd
#   就等价于「用户在那个位置能看到任务栏」。
#
# 同时打印游戏壁纸窗的 Z 序作为参照：任务栏排名小于壁纸窗 = 在壁纸之上 = 可见。
param([switch]$PressF11, [string]$Tag = "", [switch]$WinD)

Add-Type -Namespace Tw -Name W -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
[DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint ga);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
public delegate bool EnumProc(IntPtr h, IntPtr l);
'@

function Cls($h) { if ($h -eq [IntPtr]::Zero) { return "(无)" }
    $sb = New-Object System.Text.StringBuilder 256; [void][Tw.W]::GetClassName($h, $sb, 256)
    $c = $sb.ToString(); if ($c -eq "") { $c = "?" }; return $c }

function Rank($target) {
    $script:i = 0; $script:f = -1
    $cb = [Tw.W+EnumProc]{ param($h,$l) if ($h -eq $target) { $script:f = $script:i }; $script:i++; return $true }
    [void][Tw.W]::EnumWindows($cb, [IntPtr]::Zero); return $script:f }

# 游戏壁纸窗
$script:game = [IntPtr]::Zero
$cbg = [Tw.W+EnumProc]{
    param($h, $l)
    $procId = 0; [void][Tw.W]::GetWindowThreadProcessId($h, [ref]$procId)
    $pn = "?"; try { $pn = (Get-Process -Id $procId -ErrorAction Stop).ProcessName } catch { }
    if ($pn -like "Chill With You*") {
        $c = New-Object System.Text.StringBuilder 256; [void][Tw.W]::GetClassName($h, $c, 256)
        if ($c.ToString() -eq "UnityWndClass") { $script:game = $h }
    }
    return $true
}

$SW = [Tw.W]::GetSystemMetrics(0); $SH = [Tw.W]::GetSystemMetrics(1)

if ($WinD) {
    [void][Tw.W]::keybd_event(0x5B,0,0,[System.UIntPtr]::Zero); Start-Sleep -Milliseconds 50
    [void][Tw.W]::keybd_event(0x44,0,0,[System.UIntPtr]::Zero); Start-Sleep -Milliseconds 50
    [void][Tw.W]::keybd_event(0x44,0,2,[System.UIntPtr]::Zero); Start-Sleep -Milliseconds 50
    [void][Tw.W]::keybd_event(0x5B,0,2,[System.UIntPtr]::Zero)
    Start-Sleep -Seconds 2
}

if ($PressF11) {
    [void][Tw.W]::keybd_event(0x7A,0,0,[System.UIntPtr]::Zero); Start-Sleep -Milliseconds 90
    [void][Tw.W]::keybd_event(0x7A,0,2,[System.UIntPtr]::Zero)
    Start-Sleep -Seconds 2
}

[void][Tw.W]::EnumWindows($cbg, [IntPtr]::Zero)

$tray = [Tw.W]::FindWindow("Shell_TrayWnd", $null)
$p1 = New-Object Tw.W+POINT; $p1.X = 100;  $p1.Y = $SH - 20   # 左下（开始按钮附近）
$p2 = New-Object Tw.W+POINT; $p2.X = 1500; $p2.Y = $SH - 20   # 右下（托盘区附近）
$h1 = [Tw.W]::WindowFromPoint($p1)
$h2 = [Tw.W]::WindowFromPoint($p2)
$h1t = [Tw.W]::GetAncestor($h1, 2)   # GA_ROOT
$h2t = [Tw.W]::GetAncestor($h2, 2)

$trayRank = Rank $tray
$gameRank = Rank $script:game
$onTop = ($trayRank -ge 0) -and (($gameRank -lt 0) -or ($trayRank -lt $gameRank))

Write-Host ("[{0,-10}] 任务栏: 可见={1,-6} Z序={2,-4} | 壁纸窗: Z序={3,-4} | 任务栏在壁纸之上={4}" -f `
    $Tag, [Tw.W]::IsWindowVisible($tray), $trayRank, $gameRank, $onTop)
Write-Host ("             左下命中=[{0}]  右下命中=[{1}]  （都应是 Shell_TrayWnd 之类的任务栏窗口）" -f `
    (Cls $h1t), (Cls $h2t))
