using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ChillDesktop.Desktop;

namespace ChillDesktop
{
    internal static class Program
    {
        private const string GameExe =
            @"C:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story\Chill With You.exe";

        private static DesktopLayer _layer;
        private static Process _gameProcess;
        private static NotifyIcon _tray;
        private static System.Threading.Timer _heartbeat;
        private const string QuitEventName = "Local\\ChillDesktop_Quit";
        private static int _missingCount;
        private static FolderWidget _widget;
        private static EventWaitHandle _quitEvent;

        [STAThread]
        private static void Main(string[] args)
        {
            // --quit：通知已在运行的实例优雅退出（走 Cleanup：还原游戏窗口、恢复桌面图标）
            if (Array.IndexOf(args, "--quit") >= 0)
            {
                SignalQuit();
                return;
            }

            using var mutex = new Mutex(true, "ChillDesktop_SingleInstance", out bool created);
            if (!created)
            {
                MessageBox.Show("ChillDesktop 已经在运行了（看系统托盘）。", "ChillDesktop");
                return;
            }

            // 命名事件：外部可用 ChillDesktop.exe --quit 请求退出
            _quitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);
            ThreadPool.RegisterWaitForSingleObject(_quitEvent,
                (_, __) => { try { Application.Exit(); } catch { } },
                null, -1, true);

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                _layer = new DesktopLayer();

                if (!TryAttachOrLaunch())
                {
                    MessageBox.Show("没能等到游戏窗口出现，启动器退出。", "ChillDesktop");
                    return;
                }

                _layer.EnsureLayers();
                _layer.EmbedGame();
                _layer.HideIcons();

                // 收纳夹组件（壁纸层内、右下角、游戏画面之上）
                _widget = new FolderWidget(_layer);
                _widget.ShowWidget();

                BuildTray();
                StartHeartbeat();

                Application.ApplicationExit += (s, e) => Cleanup(killGame: false);
                Application.Run();
            }
            catch (Exception ex)
            {
                Log.Error("Main", ex);
                _layer?.Detach();
                MessageBox.Show("启动失败：\n" + ex.Message, "ChillDesktop");
            }
        }

        private static void SignalQuit()
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(QuitEventName, out var ev))
                {
                    ev.Set();
                    ev.Dispose();
                }
            }
            catch { }
        }

        private static bool TryAttachOrLaunch()
        {
            var hwnd = DesktopLayer.FindGameWindow(IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                // 重连场景：游戏可能已被上一实例嵌进 Progman（不再是顶级窗口）
                var prog = Native.User32.FindWindow("Progman", "Program Manager");
                if (prog != IntPtr.Zero)
                    hwnd = DesktopLayer.FindGameWindow(prog);
            }
            if (hwnd != IntPtr.Zero)
            {
                Log.Write("检测到游戏已在运行，直接附加。");
                return true;
            }

            if (!File.Exists(GameExe))
            {
                Log.Write("默认路径找不到游戏 exe：" + GameExe);
                return false;
            }

            Log.Write("启动游戏：" + GameExe);
            _gameProcess = Process.Start(new ProcessStartInfo
            {
                FileName = GameExe,
                WorkingDirectory = Path.GetDirectoryName(GameExe),
                UseShellExecute = true,
            });

            // 等 Unity 主窗口（UnityWndClass）出现
            var deadline = DateTime.Now + TimeSpan.FromSeconds(180);
            while (DateTime.Now < deadline)
            {
                if (_gameProcess != null && _gameProcess.HasExited)
                {
                    Log.Write("游戏进程提前退出。");
                    return false;
                }
                if (DesktopLayer.FindGameWindow(IntPtr.Zero) != IntPtr.Zero)
                {
                    Log.Write("游戏主窗口已出现，等待 Unity 稳定 8 秒……");
                    Thread.Sleep(8000);
                    return true;
                }
                Thread.Sleep(1000);
            }
            return false;
        }

        private static void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("重新嵌入桌面", null, (s, e) =>
            {
                _layer.EnsureLayers();
                _layer.EmbedGame();
                _layer.HideIcons();
                _widget?.ShowWidget();
            });
            menu.Items.Add("显示/隐藏桌面图标", null, (s, e) => ToggleIcons());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出（游戏继续运行）", null, (s, e) =>
            {
                Cleanup(killGame: false);
                Application.Exit();
            });
            menu.Items.Add("退出并关闭游戏", null, (s, e) =>
            {
                Cleanup(killGame: true);
                Application.Exit();
            });

            _tray = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "ChillDesktop - 她在你的桌面上",
                Visible = true,
                ContextMenuStrip = menu,
            };
        }

        private static bool _iconsManuallyShown;

        private static void ToggleIcons()
        {
            // 简单切换：用 SHELLDLL_DefView 当前可见性取反
            var shell = _layer.ShellView;
            if (shell == IntPtr.Zero) return;
            bool vis = Native.User32.IsWindowVisible(shell);
            Native.User32.ShowWindow(shell, vis ? Native.User32.SW_HIDE : Native.User32.SW_SHOW);
            _iconsManuallyShown = !vis;
        }

        private static void StartHeartbeat()
        {
            _heartbeat = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (!_layer.Heartbeat())
                    {
                        if (++_missingCount >= 3)
                        {
                            Log.Write("游戏窗口连续丢失，判定游戏已关闭，启动器退出。");
                            _layer.RestoreIcons();
                            Application.Exit();
                        }
                        return;
                    }
                    _missingCount = 0;
                    _widget?.Heartbeat();
                }
                catch (Exception ex) { Log.Error("heartbeat", ex); }
            }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        }

        private static void Cleanup(bool killGame)
        {
            try { _heartbeat?.Dispose(); } catch { }
            try { _widget?.Dispose(); } catch { }
            try { _layer?.Detach(); } catch { }

            if (killGame)
            {
                try
                {
                    if (_gameProcess != null && !_gameProcess.HasExited)
                        _gameProcess.Kill(entireProcessTree: true);
                    else if (DesktopLayer.FindGameWindow(IntPtr.Zero) != IntPtr.Zero ||
                             DesktopLayer.FindGameWindow(_layer?.Progman ?? IntPtr.Zero) != IntPtr.Zero)
                        Process.GetProcessesByName("Chill With You").ForEach(p => p.Kill(true));
                }
                catch (Exception ex) { Log.Error("killGame", ex); }
            }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }
    }

    internal static class ProcessExt
    {
        public static void ForEach<T>(this T[] arr, Action<T> action)
        {
            if (arr == null) return;
            foreach (var x in arr) action(x);
        }
    }
}
