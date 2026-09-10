# 应急脚本：把 ChillDesktop 改过的系统状态还原回去。
#
# 插件正常退出时会自己还原；这个脚本用于游戏崩溃 / 被任务管理器强杀，
# 导致「桌面图标一直是隐藏的」或「任务栏不见了」的情况。
#
# 还原两样东西：
#   1) 桌面图标层 SHELLDLL_DefView
#   2) Windows 任务栏 Shell_TrayWnd / Shell_SecondaryTrayWnd

Add-Type -Namespace Dsh -Name Win -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
[DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
[DllImport("user32.dll", CharSet=CharSet.Unicode)]
public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int n);
[DllImport("user32.dll")]
public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")]
public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int X, int Y, int cx, int cy, uint flags);
[DllImport("user32.dll")]
public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
[DllImport("user32.dll")]
public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")]
public static extern bool IsWindow(IntPtr hWnd);
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")]
public static extern bool EnumWindows(EnumProc cb, IntPtr l);
'@

$SW_SHOW = 5
$changed = $false

# ---- 1) 桌面图标层 ----
$progman = [Dsh.Win]::FindWindow("Progman", "Program Manager")
$defview = [IntPtr]::Zero
if ($progman -ne [IntPtr]::Zero) {
    $defview = [Dsh.Win]::FindWindowEx($progman, [IntPtr]::Zero, "SHELLDLL_DefView", $null)
}
if ($defview -ne [IntPtr]::Zero) {
    if ([Dsh.Win]::IsWindowVisible($defview)) {
        Write-Host "桌面图标层：本来就是可见的，无需处理。"
    } else {
        [Dsh.Win]::ShowWindow($defview, $SW_SHOW) | Out-Null
        Write-Host "桌面图标层：已恢复。"
        $changed = $true
    }
} else {
    Write-Host "桌面图标层：没找到 SHELLDLL_DefView（可能结构不同，或本来就正常）。"
}

# ---- 2) 任务栏 ----
$trayHandles = @()
$main = [Dsh.Win]::FindWindow("Shell_TrayWnd", $null)
if ($main -ne [IntPtr]::Zero) { $trayHandles += $main }

$script:extra = @()
$cb = [Dsh.Win+EnumProc]{
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [Dsh.Win]::GetClassName($h, $sb, 256) | Out-Null
    if ($sb.ToString() -eq "Shell_SecondaryTrayWnd") { $script:extra += $h }
    return $true
}
[Dsh.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
$trayHandles += $script:extra

if ($trayHandles.Count -eq 0) {
    Write-Host "任务栏：没找到 Shell_TrayWnd（Explorer 可能没在跑）。"
} else {
    $restored = 0; $fixed = 0
    foreach ($t in $trayHandles) {
        $hidden = -not [Dsh.Win]::IsWindowVisible($t)
        $notTop = ([Dsh.Win]::GetWindowLong($t, -20) -band 0x8) -eq 0
        if ($hidden -or $notTop) {
            # 注意：ShowWindow(SW_HIDE)→SW_SHOW 会丢掉 WS_EX_TOPMOST，
            # 任务栏会"可见"却被压在普通窗口下面，所以必须一并把置顶属性补回去。
            [Dsh.Win]::SetWindowPos($t, [IntPtr](-1), 0, 0, 0, 0,
                0x0002 -bor 0x0001 -bor 0x0010 -bor 0x0040) | Out-Null   # NOMOVE|NOSIZE|NOACTIVATE|SHOWWINDOW
            if ($hidden) { $restored++ }
            if ($notTop) { $fixed++ }
        }
    }
    if ($restored -gt 0 -or $fixed -gt 0) {
        Write-Host "任务栏：已恢复 $restored 个（另修正 $fixed 个被压掉的置顶属性）。"
        $changed = $true
    } else {
        Write-Host "任务栏：本来就是可见且置顶的，无需处理。"
    }
}

if (-not $changed) { Write-Host "`n系统状态本来就是正常的，没做任何改动。" }
