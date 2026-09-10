using System;
using System.Collections.Generic;
using ChillDesktop.Native;

namespace ChillDesktop.Desktop
{
    /// <summary>
    /// 「全屏模式」：把 Windows 11 底部任务栏藏起来（F11 切换）。
    ///
    /// 做法是直接对 `Shell_TrayWnd`（主屏任务栏）和 `Shell_SecondaryTrayWnd`（副屏任务栏）
    /// 调 `ShowWindow(SW_HIDE)`。壁纸窗本来就铺满整屏，任务栏一藏，底下那条也会露出壁纸。
    ///
    /// 和「隐藏桌面图标」一样，这是我们对系统动了手脚的状态，退出时**必须**还原：
    ///   - 插件退出（含 ProcessExit / OnApplicationQuit）→ <see cref="Show"/>；
    ///   - Explorer 重启后任务栏句柄会换新 → <see cref="EnsureHidden"/> 重新找、重新藏；
    ///   - 进程被强杀来不及还原 → `tools\Restore-Desktop.ps1` 兜底。
    ///
    /// 故意**不**在心跳里无条件重新隐藏：用户按 Win 键时系统会把任务栏临时唤出来，
    /// 那是用户自己的操作，插件不该跟用户抢。只有句柄失效（Explorer 重启）才重新接管。
    /// </summary>
    internal sealed class TaskbarController
    {
        private const string MainTrayClass = "Shell_TrayWnd";
        private const string SecondaryTrayClass = "Shell_SecondaryTrayWnd";

        public bool Hidden => _hidden.Count > 0;

        /// <summary>
        /// 任务栏在隐藏前的状态。**必须记住 WS_EX_TOPMOST**：
        /// `ShowWindow(SW_HIDE)` → `SW_SHOW` 会把窗口的置顶属性丢掉、把它插回普通窗口层的最底部，
        /// 于是任务栏虽然 `IsWindowVisible=True`，却被铺满全屏的壁纸盖住——表现就是「F11 再按一次没反应」。
        /// 这是实测确认过的坑（恢复后 ExStyle 从含 0x8 变成只有 0x80，Z 序掉到 238）。
        /// </summary>
        private sealed class BarState
        {
            public IntPtr Hwnd;
            public bool WasTopmost;
        }

        private readonly List<BarState> _hidden = new List<BarState>();

        /// <summary>
        /// 任务栏「正常状态」的置顶属性基线。在非全屏模式下由心跳持续采样，
        /// 恢复时以它为准——比起「隐藏前临时读一次」，这样即使某次读到的是被系统
        /// 弄脏的值，也会被下一次正常采样纠正回来。
        /// </summary>
        private readonly Dictionary<IntPtr, bool> _baselineTopmost = new Dictionary<IntPtr, bool>();

        /// <summary>在当前（应处于正常状态）采样任务栏的置顶属性作为基线。非全屏时由心跳调用。</summary>
        public void RememberBaseline()
        {
            if (_hidden.Count > 0) return; // 正在全屏模式里，此刻的状态不是基线

            foreach (var h in FindBars())
            {
                if (!User32.IsWindowVisible(h)) continue;
                _baselineTopmost[h] = (User32.GetWindowLongPtr(h, User32.GWL_EXSTYLE).ToInt64()
                                       & User32.WS_EX_TOPMOST) != 0;
            }
        }

        private bool BaselineTopmost(IntPtr h, bool fallback)
        {
            bool v;
            return _baselineTopmost.TryGetValue(h, out v) ? v : fallback;
        }

        /// <summary>藏起所有任务栏。返回是否至少藏到一个。</summary>
        public bool Hide()
        {
            var bars = FindBars();
            if (bars.Count == 0)
            {
                Log.Write("找不到任务栏窗口（Shell_TrayWnd），无法进入全屏模式。");
                return false;
            }

            int ok = 0;
            foreach (var h in bars)
            {
                // ⚠️ 必须先读置顶属性再隐藏：ShowWindow(SW_HIDE) 当场就会把 WS_EX_TOPMOST 清掉
                // （实测：隐藏后 ExStyle 从含 0x8 变成不含，Z 序从 7 掉到 243）。
                // 在隐藏之后才读，会读到 false，恢复时就会把任务栏主动设成 NOTOPMOST、
                // 压到所有普通窗口（包括我们的壁纸窗）下面 —— 表现就是「F11 再按一次没反应」。
                bool wasTopmost = (User32.GetWindowLongPtr(h, User32.GWL_EXSTYLE).ToInt64()
                                   & User32.WS_EX_TOPMOST) != 0;

                if (User32.IsWindowVisible(h))
                {
                    User32.ShowWindow(h, User32.SW_HIDE);
                    if (!User32.IsWindowVisible(h)) ok++;
                }
                if (!_hidden.Exists(s => s.Hwnd == h))
                    _hidden.Add(new BarState { Hwnd = h, WasTopmost = wasTopmost });
            }

            Log.Write($"全屏模式：已隐藏 {ok}/{bars.Count} 个任务栏窗口" +
                      (bars.Count > 1 ? "（含副屏）" : "") + (_hidden.Count > 0 && ok == 0 ? "（原本就不可见）" : ""));
            return _hidden.Count > 0;
        }

        /// <summary>还原所有任务栏（含置顶属性），并校验；不成功就重试几次。</summary>
        public void Show()
        {
            if (_hidden.Count == 0) return;

            int n = 0;
            foreach (var s in _hidden)
            {
                if (s.Hwnd == IntPtr.Zero || !User32.IsWindow(s.Hwnd)) continue;

                bool wantTopmost = BaselineTopmost(s.Hwnd, s.WasTopmost);
                IntPtr target = wantTopmost ? User32.HWND_TOPMOST : User32.HWND_NOTOPMOST;
                const uint flags = User32.SWP_NOMOVE | User32.SWP_NOSIZE |
                                   User32.SWP_NOACTIVATE | User32.SWP_SHOWWINDOW;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    // ⚠️ 必须「显示 + 置顶」一次原子完成。
                    // 先 ShowWindow(SW_SHOW) 再 SetWindowPos(HWND_TOPMOST) 是不行的：
                    // 跨线程的 ShowWindow 是异步的（投递到 Explorer 线程），等它真正落地时
                    // 会按「底部插入」重建 z 序，把我们刚设好的置顶又掀掉
                    // —— 实测恢复后 ExStyle 丢掉 0x8、Z 序掉到 240，任务栏就看不见了。
                    User32.SetWindowPos(s.Hwnd, target, 0, 0, 0, 0, flags);

                    bool visible = User32.IsWindowVisible(s.Hwnd);
                    bool topOk = !wantTopmost ||
                                 ((User32.GetWindowLongPtr(s.Hwnd, User32.GWL_EXSTYLE).ToInt64()
                                   & User32.WS_EX_TOPMOST) != 0);
                    if (visible && topOk) break;
                    System.Threading.Thread.Sleep(80);
                }

                // 最后再兜一次：不再动显示状态，只把置顶属性钉死
                if (wantTopmost && User32.IsWindow(s.Hwnd) &&
                    (User32.GetWindowLongPtr(s.Hwnd, User32.GWL_EXSTYLE).ToInt64()
                     & User32.WS_EX_TOPMOST) == 0)
                {
                    User32.SetWindowPos(s.Hwnd, User32.HWND_TOPMOST, 0, 0, 0, 0,
                        User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
                }

                if (User32.IsWindowVisible(s.Hwnd)) n++;
            }

            _hidden.Clear();
            Log.Write($"全屏模式：已恢复 {n} 个任务栏窗口（含置顶属性）");
        }

        /// <summary>
        /// 心跳调用：只在「原先藏的那批句柄失效了」时重新接管（典型场景是 Explorer 重启）。
        /// 用户自己按 Win 键把任务栏唤出来时不做任何事。
        /// </summary>
        public void EnsureHidden()
        {
            if (_hidden.Count == 0) return;

            bool stale = false;
            foreach (var s in _hidden)
            {
                if (s.Hwnd == IntPtr.Zero || !User32.IsWindow(s.Hwnd)) { stale = true; break; }
            }
            if (!stale) return;

            Log.Write("检测到任务栏窗口句柄失效（Explorer 重启？），重新接管全屏模式。");
            _hidden.Clear();
            Hide();
        }

        // ---------- 查找 ----------

        private static List<IntPtr> FindBars()
        {
            var list = new List<IntPtr>();

            var main = User32.FindWindow(MainTrayClass, null);
            if (main != IntPtr.Zero) list.Add(main);

            User32.EnumWindows((h, _) =>
            {
                if (ClassOf(h) == SecondaryTrayClass && !list.Contains(h))
                    list.Add(h);
                return true;
            }, IntPtr.Zero);

            return list;
        }

        private static string ClassOf(IntPtr h)
        {
            var sb = new System.Text.StringBuilder(256);
            User32.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // ---------- 按键 ----------

        /// <summary>
        /// 把 cfg 里的按键名解析成 Windows 虚拟键码。
        /// 支持 "F11"~"F24"、"A"~"Z"、"0"~"9"，以及 "NONE"/空 = 关闭。
        /// </summary>
        public static int ParseVirtualKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string s = name.Trim().ToUpperInvariant();
            if (s == "NONE" || s == "DISABLED" || s == "OFF") return 0;

            if (s.Length >= 2 && s[0] == 'F')
            {
                int n;
                if (int.TryParse(s.Substring(1), out n) && n >= 1 && n <= 24)
                    return 0x70 + (n - 1);
            }
            if (s.Length == 1)
            {
                char c = s[0];
                if (c >= 'A' && c <= 'Z') return c;              // VK_A..VK_Z 正好等于 ASCII
                if (c >= '0' && c <= '9') return 0x30 + (c - '0');
            }
            return 0;
        }
    }
}
