# 应急脚本：恢复被隐藏的桌面图标层（SHELLDLL_DefView）。
# 插件正常情况下退出时会自己恢复；这个脚本用于游戏崩溃/被强杀导致图标没回来的情况。
# 也可以直接重启 explorer.exe 达到同样效果，但那样会关掉所有资源管理器窗口。

Add-Type -Namespace Dsh -Name Win -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
[DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
[DllImport("user32.dll")]
public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")]
public static extern bool IsWindowVisible(IntPtr hWnd);
'@

$progman = [Dsh.Win]::FindWindow("Progman", "Program Manager")
$defview = [IntPtr]::Zero
if ($progman -ne [IntPtr]::Zero) {
    $defview = [Dsh.Win]::FindWindowEx($progman, [IntPtr]::Zero, "SHELLDLL_DefView", $null)
}

if ($defview -eq [IntPtr]::Zero) {
    Write-Host "没找到 SHELLDLL_DefView（可能图标层结构不同，或本来就正常）。"
    exit 0
}

if ([Dsh.Win]::IsWindowVisible($defview)) {
    Write-Host "桌面图标层本来就是可见的，无需恢复。"
} else {
    [Dsh.Win]::ShowWindow($defview, 5) | Out-Null
    Write-Host "已恢复桌面图标层。"
}
