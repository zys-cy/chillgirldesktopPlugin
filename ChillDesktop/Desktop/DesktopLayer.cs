using System;
using System.Text;
using ChillDesktop.Native;

namespace ChillDesktop.Desktop
{
    /// <summary>
    /// 把游戏窗口变成「可交互的动态桌面」：顶级无边框窗口铺满主屏、用 HWND_BOTTOM 沉到
    /// 普通窗口堆底，并加 WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE。
    ///
    /// 为什么不用 WorkerW / Progman 子窗口（Wallpaper Engine 常见做法）：
    /// 24H2 下挂进 Progman 的子窗口虽然能当壁纸、不被 Win+D 最小化，但【收不到真实鼠标点击】
    /// （几何命中却不投递输入，已实测），导致游戏原生 UI（笔记/待办/日历等）全部失效。
    /// 顶级 + HWND_BOTTOM + NOACTIVATE 的组合（Rainmeter 桌面挂件同款）三者兼得：
    ///   - 真实点击正常投递给游戏（Unity 用全局鼠标状态，无需窗口焦点）；
    ///   - NOACTIVATE：不抢前台焦点、点击不会把游戏抬到办公窗口之上；
    ///   - NOACTIVATE 窗口不会被 Win+D /「显示桌面」最小化。
    /// 仅负责隐藏 SHELLDLL_DefView（桌面图标），与游戏窗口解耦。
    /// </summary>
    internal sealed class DesktopLayer
    {
        public const string GameClassName = "UnityWndClass";
        public const string GameTitle = "Chill With You";

        public IntPtr GameHwnd { get; private set; }
        public IntPtr Progman { get; private set; }
        public IntPtr ShellView { get; private set; } // SHELLDLL_DefView（图标层）

        private int _origStyle;
        private int _origExStyle;
        private User32.RECT _origRect;
        private bool _iconsHidden;

        // ---------- 查找游戏窗口 ----------

        public static IntPtr FindGameWindow(IntPtr scope)
        {
            // scope == IntPtr.Zero：枚举顶级；否则枚举该父窗口的子窗口（兼容旧版曾挂进 Progman）
            IntPtr found = IntPtr.Zero;
            User32.EnumWindowsProc cb = (h, _) =>
            {
                if (scope == IntPtr.Zero && !User32.IsWindowVisible(h))
                    return true;
                if (GetClass(h) == GameClassName &&
                    GetTitle(h).IndexOf("chill", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = h;
                    return false;
                }
                return true;
            };

            if (scope == IntPtr.Zero)
            {
                var exact = User32.FindWindow(GameClassName, GameTitle);
                if (exact != IntPtr.Zero)
                    return exact;
                User32.EnumWindows(cb, IntPtr.Zero);
            }
            else
            {
                User32.EnumChildWindows(scope, cb, IntPtr.Zero);
            }
            return found;
        }

        private static string GetTitle(IntPtr h)
        {
            var sb = new StringBuilder(256);
            User32.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetClass(IntPtr h)
        {
            var sb = new StringBuilder(256);
            User32.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // ---------- 桌面图标层定位 ----------

        public void EnsureLayers()
        {
            Progman = User32.FindWindow("Progman", "Program Manager");
            ShellView = IntPtr.Zero;

            if (Progman != IntPtr.Zero)
                ShellView = User32.FindWindowEx(Progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            // 老系统：SHELLDLL_DefView 在某个顶级 WorkerW 下
            if (ShellView == IntPtr.Zero)
            {
                User32.EnumWindows((top, _) =>
                {
                    var sv = User32.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (sv != IntPtr.Zero) { ShellView = sv; return false; }
                    return true;
                }, IntPtr.Zero);
            }

            Log.Write($"桌面层: progman={Progman} shell={ShellView}（顶级沉底可交互方案）");
        }

        // ---------- 嵌入 / 还原 ----------

        public void EmbedGame()
        {
            GameHwnd = FindGameWindow(IntPtr.Zero);
            if (GameHwnd == IntPtr.Zero && Progman != IntPtr.Zero)
                GameHwnd = FindGameWindow(Progman); // 兼容旧版/重连：曾被挂进 Progman
            if (GameHwnd == IntPtr.Zero)
                throw new InvalidOperationException("找不到游戏窗口");

            User32.GetWindowRect(GameHwnd, out _origRect);
            _origStyle = User32.GetWindowLong(GameHwnd, User32.GWL_STYLE);
            _origExStyle = User32.GetWindowLong(GameHwnd, User32.GWL_EXSTYLE);

            // 顶级、无边框、无标题栏、不可调整大小
            int style = _origStyle;
            style &= ~(User32.WS_CHILD | User32.WS_POPUP | User32.WS_CAPTION |
                       User32.WS_THICKFRAME | User32.WS_MINIMIZEBOX | User32.WS_MAXIMIZEBOX);
            style |= User32.WS_POPUP | User32.WS_VISIBLE | User32.WS_CLIPSIBLINGS;
            User32.SetWindowLong(GameHwnd, User32.GWL_STYLE, style);

            // LAYERED：Unity D3D11 flip-model 加 alpha=255 的独立合成；TOOLWINDOW：移出任务栏/Alt+Tab；
            // NOACTIVATE：可点但不抢焦点、不抬升、不被 Win+D 最小化（关键）。
            int ex = _origExStyle;
            ex |= User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE;
            User32.SetWindowLong(GameHwnd, User32.GWL_EXSTYLE, ex);
            User32.SetLayeredWindowAttributes(GameHwnd, 0, 255, User32.LWA_ALPHA);

            // 脱离任何桌面宿主（旧版可能挂在 Progman 下）
            if (User32.GetParent(GameHwnd) != IntPtr.Zero)
                User32.SetParent(GameHwnd, IntPtr.Zero);

            User32.SetWindowPos(GameHwnd, User32.HWND_BOTTOM, 0, 0, ScreenW(), ScreenH(),
                User32.SWP_SHOWWINDOW | User32.SWP_NOACTIVATE);
            User32.ShowWindow(GameHwnd, User32.SW_SHOWNOACTIVATE);

            Log.Write($"游戏已铺为可交互桌面 hwnd={GameHwnd} {ScreenW()}x{ScreenH()}");
        }

        public void Detach()
        {
            try
            {
                if (GameHwnd != IntPtr.Zero && User32.IsWindow(GameHwnd))
                {
                    if (_origStyle != 0)
                        User32.SetWindowLong(GameHwnd, User32.GWL_STYLE, _origStyle);
                    if (_origExStyle != 0)
                        User32.SetWindowLong(GameHwnd, User32.GWL_EXSTYLE, _origExStyle);
                    User32.SetWindowPos(GameHwnd, IntPtr.Zero,
                        _origRect.Left, _origRect.Top, _origRect.Width, _origRect.Height,
                        User32.SWP_SHOWWINDOW);
                    User32.ShowWindow(GameHwnd, User32.SW_RESTORE);
                    Log.Write("游戏窗口已还原为普通窗口");
                }
            }
            catch (Exception ex) { Log.Error("Detach", ex); }
            finally { RestoreIcons(); }
        }

        // ---------- 桌面图标 ----------

        public void HideIcons()
        {
            if (ShellView != IntPtr.Zero && User32.IsWindow(ShellView))
            {
                User32.ShowWindow(ShellView, User32.SW_HIDE);
                _iconsHidden = true;
                Log.Write("桌面图标已隐藏");
            }
        }

        public void RestoreIcons()
        {
            if (_iconsHidden && ShellView != IntPtr.Zero && User32.IsWindow(ShellView))
            {
                User32.ShowWindow(ShellView, User32.SW_SHOW);
                _iconsHidden = false;
                Log.Write("桌面图标已恢复");
            }
        }

        // ---------- 自愈 ----------

        /// <summary>定时心跳：游戏脱离/尺寸变化/Explorer 重启后纠正。返回游戏窗口是否仍在。</summary>
        public bool Heartbeat()
        {
            if (GameHwnd == IntPtr.Zero || !User32.IsWindow(GameHwnd))
                return false;

            // Explorer 重启 → 重新定位图标层并继续隐藏
            if (ShellView == IntPtr.Zero || !User32.IsWindow(ShellView))
            {
                Log.Write("检测到桌面层重建（Explorer 重启？），重新定位图标层……");
                EnsureLayers();
                if (_iconsHidden) HideIcons();
            }

            bool needReembed = false;
            if (User32.GetParent(GameHwnd) != IntPtr.Zero)
                needReembed = true; // 被别的东西重新挂走了

            User32.GetWindowRect(GameHwnd, out var r);
            if (r.Left != 0 || r.Top != 0 || r.Width != ScreenW() || r.Height != ScreenH())
                needReembed = true;

            // 万一被最小化（正常 NOACTIVATE 不会），恢复
            if (User32.IsIconic(GameHwnd))
                needReembed = true;

            if (needReembed)
            {
                Log.Write("游戏窗口状态偏移，重新铺底……");
                EmbedGame();
            }
            return true;
        }

        public static int ScreenW() => User32.GetSystemMetrics(User32.SM_CXSCREEN);
        public static int ScreenH() => User32.GetSystemMetrics(User32.SM_CYSCREEN);
    }
}
