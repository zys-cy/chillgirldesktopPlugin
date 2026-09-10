using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using ChillDesktop.Native;

namespace ChillDesktop.Desktop
{
    /// <summary>
    /// 把游戏自己的窗口变成「可交互的动态桌面」：顶级无边框窗口铺满主屏、HWND_BOTTOM 沉到
    /// 普通窗口堆底，并加 WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE。
    ///
    /// 为什么不用 WorkerW / Progman 子窗口（Wallpaper Engine 常见做法）：
    /// 24H2 下挂进 Progman 的子窗口虽然能当壁纸、不被 Win+D 最小化，但【收不到真实鼠标点击】
    /// （几何命中却不投递输入，已实测），游戏原生 UI（笔记/待办/日历等）全部失效。
    /// 顶级 + HWND_BOTTOM + NOACTIVATE 的组合（Rainmaker 桌面挂件同款）三者兼得。
    ///
    /// 插件版与原型启动器的差异：
    ///   - 不再跨进程找窗口，改为「本进程自己的 UnityWndClass 顶级窗」；
    ///   - 原始样式/矩形只在第一次嵌入前抓一次（原型每次 EmbedGame 都覆盖，
    ///     心跳自愈几次后 Detach 就还原不回原状了——插件版已修正）；
    ///   - 屏幕尺寸优先用 Unity 的 Display.main.systemWidth/Height（物理像素），
    ///     避免进程 DPI 感知方式不同导致 GetSystemMetrics 返回虚拟化尺寸。
    /// </summary>
    internal sealed class DesktopLayer
    {
        public const string GameClassName = "UnityWndClass";

        /// <summary>屏幕物理像素提供者，由 Plugin 注入（Unity 侧）。返回空则退回 GetSystemMetrics。</summary>
        public static Func<Size> ScreenSizeProvider;

        public IntPtr GameHwnd { get; private set; }
        public IntPtr Progman { get; private set; }
        public IntPtr ShellView { get; private set; } // SHELLDLL_DefView（图标层）

        private bool _styleCaptured;
        private int _origStyle;
        private int _origExStyle;
        private User32.RECT _origRect;
        private bool _iconsHidden;

        public bool StyleCaptured => _styleCaptured;
        public int OrigStyle => _origStyle;
        public int OrigExStyle => _origExStyle;
        /// <summary>桌面图标层当前是否处于本插件隐藏的状态。</summary>
        public bool IconsHidden => _iconsHidden;

        // ---------- 查找游戏窗口 ----------

        /// <summary>找本进程自己的 Unity 主窗口（顶级、可见、面积最大者优先）。</summary>
        public static IntPtr FindOwnMainWindow()
        {
            uint pid = (uint)Process.GetCurrentProcess().Id;
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;

            User32.EnumWindowsProc cb = (h, _) =>
            {
                uint wpid;
                User32.GetWindowThreadProcessId(h, out wpid);
                if (wpid != pid) return true;
                if (!User32.IsWindowVisible(h)) return true;
                if (User32.GetParent(h) != IntPtr.Zero) return true; // 只要顶级

                bool isUnity = GetClass(h) == GameClassName;
                User32.GetWindowRect(h, out var r);
                long area = (long)r.Width * r.Height;

                // UnityWndClass 优先，否则取最大的可见顶级窗
                if (isUnity)
                {
                    if (best == IntPtr.Zero || !IsUnityWindow(best) || area > bestArea)
                    {
                        best = h; bestArea = area;
                    }
                }
                else if (best == IntPtr.Zero || (!IsUnityWindow(best) && area > bestArea))
                {
                    best = h; bestArea = area;
                }
                return true;
            };

            User32.EnumWindows(cb, IntPtr.Zero);
            return best;
        }

        private static bool IsUnityWindow(IntPtr h) => h != IntPtr.Zero && GetClass(h) == GameClassName;

        private static string GetClass(IntPtr h)
        {
            var sb = new StringBuilder(256);
            User32.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetTitle(IntPtr h)
        {
            var sb = new StringBuilder(256);
            User32.GetWindowText(h, sb, sb.Capacity);
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

            Log.Write($"桌面层: progman={Progman} shell={ShellView}");
        }

        // ---------- 嵌入 / 还原 ----------

        public void EmbedGame()
        {
            var hwnd = FindOwnMainWindow();
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("找不到本进程的游戏窗口");

            if (GameHwnd != hwnd)
            {
                GameHwnd = hwnd;
                _styleCaptured = false; // 窗口换了，重新抓原始状态
            }

            if (!_styleCaptured)
            {
                User32.GetWindowRect(GameHwnd, out _origRect);
                _origStyle = User32.GetWindowLong(GameHwnd, User32.GWL_STYLE);
                _origExStyle = User32.GetWindowLong(GameHwnd, User32.GWL_EXSTYLE);
                _styleCaptured = true;
                Log.Write($"已记录游戏窗口原始状态 style=0x{_origStyle:X8} ex=0x{_origExStyle:X8} " +
                          $"rect=({_origRect.Left},{_origRect.Top},{_origRect.Width},{_origRect.Height})");
            }

            // 顶级、无边框、无标题栏、不可调整大小
            int style = _origStyle;
            style &= ~(User32.WS_CHILD | User32.WS_POPUP | User32.WS_CAPTION |
                       User32.WS_THICKFRAME | User32.WS_MINIMIZEBOX | User32.WS_MAXIMIZEBOX);
            style |= User32.WS_POPUP | User32.WS_VISIBLE | User32.WS_CLIPSIBLINGS;
            User32.SetWindowLong(GameHwnd, User32.GWL_STYLE, style);

            // LAYERED：Unity D3D11 flip-model 加 alpha=255 的独立合成；TOOLWINDOW：移出任务栏/Alt+Tab；
            // NOACTIVATE：可点但不抢焦点、不抬升。
            int ex = _origExStyle;
            ex |= User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE;
            User32.SetWindowLong(GameHwnd, User32.GWL_EXSTYLE, ex);
            User32.SetLayeredWindowAttributes(GameHwnd, 0, 255, User32.LWA_ALPHA);

            // 脱离任何桌面宿主（旧状态可能挂在 Progman 下）
            if (User32.GetParent(GameHwnd) != IntPtr.Zero)
                User32.SetParent(GameHwnd, IntPtr.Zero);

            User32.SetWindowPos(GameHwnd, User32.HWND_BOTTOM, 0, 0, ScreenW(), ScreenH(),
                User32.SWP_SHOWWINDOW | User32.SWP_NOACTIVATE);
            User32.ShowWindow(GameHwnd, User32.SW_SHOWNOACTIVATE);
        }

        public void Detach()
        {
            try
            {
                if (GameHwnd != IntPtr.Zero && User32.IsWindow(GameHwnd))
                {
                    if (_styleCaptured)
                    {
                        User32.SetWindowLong(GameHwnd, User32.GWL_STYLE, _origStyle);
                        User32.SetWindowLong(GameHwnd, User32.GWL_EXSTYLE, _origExStyle);
                        User32.SetWindowPos(GameHwnd, IntPtr.Zero,
                            _origRect.Left, _origRect.Top, _origRect.Width, _origRect.Height,
                            User32.SWP_SHOWWINDOW);
                    }
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
            {
                // 窗口句柄失效：可能是 Unity 重建了窗口（切分辨率/全屏模式）
                var hwnd = FindOwnMainWindow();
                if (hwnd == IntPtr.Zero) return false;
                Log.Write("游戏窗口句柄已变化，重新接管");
                GameHwnd = hwnd;
                _styleCaptured = false;
                EmbedGame();
                return true;
            }

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

            if (User32.IsIconic(GameHwnd))
                needReembed = true;

            if (needReembed)
            {
                Log.Write("游戏窗口状态偏移，重新铺底……");
                EmbedGame();
            }
            return true;
        }

        // ---------- 尺寸 ----------

        public static int ScreenW() => ScreenSize().Width;
        public static int ScreenH() => ScreenSize().Height;

        public static Size ScreenSize()
        {
            try
            {
                var p = ScreenSizeProvider;
                if (p != null)
                {
                    var s = p();
                    if (s.Width > 0 && s.Height > 0) return s;
                }
            }
            catch { }
            return new Size(User32.GetSystemMetrics(User32.SM_CXSCREEN),
                            User32.GetSystemMetrics(User32.SM_CYSCREEN));
        }
    }
}
