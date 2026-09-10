# Win+D 空桌面问题的现场取证脚本（外部观察者视角）。
# 用法：
#   powershell -ExecutionPolicy Bypass -File poc\wind_probe.ps1                      # 按 Win+D 并采样 8 秒
#   powershell -ExecutionPolicy Bypass -File poc\wind_probe.ps1 -Seconds 3 -NoKey    # 只看状态，不按键
#   powershell -ExecutionPolicy Bypass -File poc\wind_probe.ps1 -NoKey              # 再按一次 Win+D（恢复桌面）
param(
    [int]$Seconds = 8,
    [switch]$NoKey
)

Add-Type -Namespace Cd -Name Win -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
public delegate bool EnumProc(IntPtr h, IntPtr l);
'@

# 注意：不要用 FindWindow("UnityWndClass", $null)——
# PowerShell 会把 $null 转成空字符串，等于"按空标题查找"，永远找不到。
# 这里按进程名枚举，最稳。
function Get-GameWindow {
    $script:found = [IntPtr]::Zero
    $script:area = -1
    $cb = [Cd.Win+EnumProc]{
        param($h, $l)
        $procId = 0
        [Cd.Win]::GetWindowThreadProcessId($h, [ref]$procId) | Out-Null
        $pn = "?"
        try { $pn = (Get-Process -Id $procId -ErrorAction Stop).ProcessName } catch { }
        if ($pn -like "Chill With You*") {
            $c = New-Object System.Text.StringBuilder 256
            [Cd.Win]::GetClassName($h, $c, 256) | Out-Null
            if ($c.ToString() -eq "UnityWndClass") {
                $r = New-Object Cd.Win+RECT
                [Cd.Win]::GetWindowRect($h, [ref]$r) | Out-Null
                $a = ($r.Right - $r.Left) * ($r.Bottom - $r.Top)
                if ($a -gt $script:area) { $script:area = $a; $script:found = $h }
            }
        }
        return $true
    }
    [Cd.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:found
}

function Send-WinD {
    [Cd.Win]::keybd_event(0x5B, 0, 0, [System.UIntPtr]::Zero)  # LWIN down
    Start-Sleep -Milliseconds 40
    [Cd.Win]::keybd_event(0x44, 0, 0, [System.UIntPtr]::Zero)  # D down
    Start-Sleep -Milliseconds 40
    [Cd.Win]::keybd_event(0x44, 0, 2, [System.UIntPtr]::Zero)  # D up
    [Cd.Win]::keybd_event(0x5B, 0, 2, [System.UIntPtr]::Zero)  # LWIN up
}

function Format-State([IntPtr]$h) {
    if ($h -eq [IntPtr]::Zero) { return "游戏窗口: 未找到" }
    if (-not [Cd.Win]::IsWindow($h)) { return "游戏窗口: 句柄已失效" }
    $r = New-Object Cd.Win+RECT
    [Cd.Win]::GetWindowRect($h, [ref]$r) | Out-Null
    $fg = [Cd.Win]::GetForegroundWindow()
    $sb = New-Object System.Text.StringBuilder 256
    [Cd.Win]::GetClassName($fg, $sb, 256) | Out-Null
    $cls = $sb.ToString()
    $sb2 = New-Object System.Text.StringBuilder 256
    [Cd.Win]::GetWindowText($fg, $sb2, 256) | Out-Null
    $title = $sb2.ToString()
    $owner = [Cd.Win]::GetWindow($h, 4)  # GW_OWNER
    return ("iconic={0,-5} visible={1,-5} rect=({2},{3},{4},{5}) owner=0x{6:X} 前台=[{7}] {8}" -f `
        [Cd.Win]::IsIconic($h), [Cd.Win]::IsWindowVisible($h), `
        $r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top), $owner.ToInt64(), $cls, $title)
}

$h = Get-GameWindow
if ($h -eq [IntPtr]::Zero) { Write-Host "没找到游戏窗口（UnityWndClass），先启动游戏。"; exit 1 }

Write-Host ("游戏 hwnd = 0x{0:X}" -f $h.ToInt64())
Write-Host ("[T-0]  " + (Format-State $h))

if (-not $NoKey) {
    Write-Host ">>> 按 Win+D"
    Send-WinD
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    $t = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    Write-Host ("[T+{0,5}] {1}" -f $t, (Format-State $h))
    Start-Sleep -Milliseconds 100
}
