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

        private readonly List<IntPtr> _hidden = new List<IntPtr>();

        public bool Hidden => _hidden.Count > 0;

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
                if (User32.IsWindowVisible(h))
                {
                    User32.ShowWindow(h, User32.SW_HIDE);
                    if (!User32.IsWindowVisible(h)) ok++;
                }
                if (!_hidden.Contains(h)) _hidden.Add(h);
            }

            Log.Write($"全屏模式：已隐藏 {ok}/{bars.Count} 个任务栏窗口" +
                      (bars.Count > 1 ? "（含副屏）" : "") + (_hidden.Count > 0 && ok == 0 ? "（原本就不可见）" : ""));
            return _hidden.Count > 0;
        }

        /// <summary>还原所有任务栏。</summary>
        public void Show()
        {
            if (_hidden.Count == 0) return;

            int n = 0;
            foreach (var h in _hidden)
            {
                if (h != IntPtr.Zero && User32.IsWindow(h))
                {
                    User32.ShowWindow(h, User32.SW_SHOW);
                    n++;
                }
            }
            _hidden.Clear();
            Log.Write($"全屏模式：已恢复 {n} 个任务栏窗口");
        }

        /// <summary>
        /// 心跳调用：只在「原先藏的那批句柄失效了」时重新接管（典型场景是 Explorer 重启）。
        /// 用户自己按 Win 键把任务栏唤出来时不做任何事。
        /// </summary>
        public void EnsureHidden()
        {
            if (_hidden.Count == 0) return;

            bool stale = false;
            foreach (var h in _hidden)
            {
                if (h == IntPtr.Zero || !User32.IsWindow(h)) { stale = true; break; }
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
