using System;
using System.Runtime.InteropServices;
using ChillDesktop.Native;

namespace ChillDesktop.Desktop
{
    /// <summary>
    /// Win+D（显示桌面 / MinimizeAll）防御。插件运行在游戏进程内，所以能直接
    /// SetWindowLongPtr(GWLP_WNDPROC) 给游戏自己的窗口装钩子——这是跨进程启动器做不到的。
    ///
    /// 职责：
    ///   1) 记录游戏窗口收到的所有「可能让它消失」的消息（WM_SYSCOMMAND / WM_SIZE /
    ///      WM_SHOWWINDOW），这是 Win+D 问题的现场取证；
    ///   2) BlockMinimize 打开时直接吃掉 SC_MINIMIZE，让窗口根本不会被最小化；
    ///   3) 可选给游戏窗口挂一个隐藏属主窗（被属主持有的窗口会被 MinimizeAll 跳过）。
    ///
    /// 钩子必须绝对安全：任何异常都不能越过托管/原生边界（会直接崩游戏），所以整体 try/catch，
    /// 且无论如何最后都调用原来的窗口过程。
    /// </summary>
    internal sealed class WindowGuard : IDisposable
    {
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const string OwnerClassName = "ChillDesktopOwnerWnd";

        // 静态字段：委托必须存活，否则 GC 后原生回调变野指针直接崩游戏
        private static WndProcDelegate _hook;
        private static WindowGuard _current;

        private IntPtr _hwnd = IntPtr.Zero;
        private IntPtr _origProc = IntPtr.Zero;
        private bool _attached;

        private IntPtr _ownerHwnd = IntPtr.Zero;
        private bool _ownerAttached;

        /// <summary>是否吞掉 SC_MINIMIZE。</summary>
        public bool BlockMinimize = true;

        /// <summary>是否给游戏窗口挂隐藏属主窗（MinimizeAll 会跳过被属主持有的窗口）。</summary>
        public bool UseOwnerWindow;

        /// <summary>钩子观察到「窗口可能要消失」时置位，由 Update 循环统一处理（不在钩子里做重活）。</summary>
        public volatile bool RestoreRequested;

        /// <summary>已拦截到的 SC_MINIMIZE 次数（取证用）。</summary>
        public int BlockedMinimizeCount;

        public bool Attached => _attached;

        // ---------- 安装 / 卸载 ----------

        public bool Attach(IntPtr hwnd)
        {
            if (_attached && _hwnd == hwnd) return true;
            if (_attached) Detach();

            if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd)) return false;

            try
            {
                _current = this;
                _hook = HookProc; // 保活

                IntPtr fp = Marshal.GetFunctionPointerForDelegate(_hook);
                Marshal.GetLastWin32Error(); // 清空 last error
                IntPtr prev = User32.SetWindowLongPtr(hwnd, User32.GWLP_WNDPROC, fp);
                int err = Marshal.GetLastWin32Error();
                if (prev == IntPtr.Zero && err != 0)
                {
                    Log.Write($"窗口钩子安装失败 err={err}");
                    _hook = null;
                    if (ReferenceEquals(_current, this)) _current = null;
                    return false;
                }

                _hwnd = hwnd;
                _origProc = prev;
                _attached = true;
                Log.Write($"窗口钩子已安装 hwnd={hwnd} 原过程=0x{prev.ToInt64():X}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("WindowGuard.Attach", ex);
                _hook = null;
                if (ReferenceEquals(_current, this)) _current = null;
                return false;
            }
        }

        public void Detach()
        {
            try
            {
                if (_ownerAttached && _hwnd != IntPtr.Zero && User32.IsWindow(_hwnd))
                {
                    User32.SetWindowLongPtr(_hwnd, User32.GWLP_HWNDPARENT, IntPtr.Zero);
                    Log.Write("已解除游戏窗口的属主关系");
                }
            }
            catch (Exception ex) { Log.Error("WindowGuard.DetachOwner", ex); }
            _ownerAttached = false;

            try
            {
                if (_attached && _hwnd != IntPtr.Zero && User32.IsWindow(_hwnd) && _origProc != IntPtr.Zero)
                {
                    User32.SetWindowLongPtr(_hwnd, User32.GWLP_WNDPROC, _origProc);
                    Log.Write("窗口钩子已卸载");
                }
            }
            catch (Exception ex) { Log.Error("WindowGuard.Detach", ex); }
            _attached = false;
            _hwnd = IntPtr.Zero;
            _origProc = IntPtr.Zero;
            _hook = null;
            if (ReferenceEquals(_current, this)) _current = null;

            DestroyOwnerWindow();
        }

        // ---------- 属主窗 ----------

        /// <summary>
        /// 给游戏窗口挂一个隐藏属主窗。MinimizeAll 会跳过「有属主」的窗口，
        /// 所以理论上 Win+D 会直接无视我们的壁纸窗。跨进程做不了这件事（属主必须同进程）。
        /// </summary>
        public bool AttachOwnerWindow(IntPtr gameHwnd)
        {
            if (_ownerAttached) return true;
            try
            {
                EnsureOwnerClassRegistered();

                _ownerHwnd = User32.CreateWindowEx(
                    User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE,
                    OwnerClassName, "ChillDesktopOwner",
                    User32.WS_POPUP,
                    0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

                if (_ownerHwnd == IntPtr.Zero)
                {
                    Log.Write("属主窗创建失败 err=" + Marshal.GetLastWin32Error());
                    return false;
                }

                User32.SetWindowLongPtr(gameHwnd, User32.GWLP_HWNDPARENT, _ownerHwnd);
                _ownerAttached = true;
                Log.Write($"游戏窗口已挂隐藏属主 hwnd={_ownerHwnd}（Win+D 免疫尝试）");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("WindowGuard.AttachOwnerWindow", ex);
                return false;
            }
        }

        private void DestroyOwnerWindow()
        {
            try
            {
                if (_ownerHwnd != IntPtr.Zero && User32.IsWindow(_ownerHwnd))
                    User32.DestroyWindow(_ownerHwnd);
            }
            catch { }
            _ownerHwnd = IntPtr.Zero;
        }

        private static bool _ownerClassRegistered;
        private static WndProcDelegate _ownerProc; // 保活

        private static void EnsureOwnerClassRegistered()
        {
            if (_ownerClassRegistered) return;
            _ownerProc = (h, m, w, l) => User32.DefWindowProc(h, m, w, l);
            var wc = new User32.WNDCLASS
            {
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_ownerProc),
                hInstance = User32.GetModuleHandleW(null),
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszClassName = OwnerClassName,
            };
            ushort atom = User32.RegisterClassW(ref wc);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
                Log.Write("属主窗 RegisterClass 失败 err=" + Marshal.GetLastWin32Error());
            _ownerClassRegistered = true;
        }

        // ---------- 钩子本体 ----------

        private IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            bool swallow = false;
            try
            {
                if (ReferenceEquals(_current, this))
                    swallow = Observe(msg, wParam);
            }
            catch (Exception ex)
            {
                try { Log.Error("WindowGuard.HookProc", ex); } catch { }
            }

            if (swallow)
                return IntPtr.Zero;

            try
            {
                if (_origProc != IntPtr.Zero)
                    return User32.CallWindowProc(_origProc, hWnd, msg, wParam, lParam);
            }
            catch { }
            return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        /// <summary>观察消息；返回 true 表示这条消息被吃掉、不再传给原窗口过程。</summary>
        private bool Observe(uint msg, IntPtr wParam)
        {
            switch (msg)
            {
                case User32.WM_SYSCOMMAND:
                    {
                        int id = User32.WmSysCommandId(wParam);
                        if (id == User32.SC_MINIMIZE)
                        {
                            RestoreRequested = true;
                            if (BlockMinimize)
                            {
                                BlockedMinimizeCount++;
                                Log.Write($"[WinD] 拦截并吃掉 SC_MINIMIZE（第 {BlockedMinimizeCount} 次）");
                                return true;
                            }
                            Log.Write("[WinD] 观察到 SC_MINIMIZE（未拦截）");
                        }
                        else if (id == User32.SC_RESTORE)
                        {
                            Log.Write("[WinD] 观察到 SC_RESTORE");
                        }
                        break;
                    }
                case User32.WM_SIZE:
                    {
                        int kind = (int)((long)wParam & 0xFFFF);
                        if (kind == User32.SIZE_MINIMIZED)
                        {
                            Log.Write("[WinD] 观察到 WM_SIZE SIZE_MINIMIZED");
                            RestoreRequested = true;
                        }
                        break;
                    }
                case User32.WM_SHOWWINDOW:
                    Log.Write($"[WinD] 观察到 WM_SHOWWINDOW wParam={wParam}");
                    break;
            }
            return false;
        }

        public void Dispose() => Detach();
    }
}
