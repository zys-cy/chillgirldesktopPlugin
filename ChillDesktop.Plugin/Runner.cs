using System;
using System.Collections;
using System.Drawing;
using ChillDesktop.Desktop;
using ChillDesktop.Native;
using UnityEngine;

namespace ChillDesktop
{
    /// <summary>
    /// 插件主循环。跑在自建的 GameObject 上（<see cref="Create"/>），
    /// 而不是 BepInEx 给插件挂的那个对象——后者在本作里会被游戏销毁。
    ///
    /// 职责：
    ///   - 等游戏主窗口出现 → 等场景稳定 → 用已验证的 Win32 配方把窗口铺成桌面壁纸；
    ///   - 200~300ms 级别的快速自愈（Win+D 把窗口弄掉时立刻拉回来）；
    ///   - 3 秒常规心跳（Explorer 重启、分辨率变化、工具箱丢失的自愈）；
    ///   - Ctrl+Alt+D 热键临时开关壁纸模式；
    ///   - 退出时还原游戏窗口与桌面图标。
    /// </summary>
    internal sealed class Runner : MonoBehaviour
    {
        private static Runner _instance;

        private DesktopLayer _layer;
        private WindowGuard _guard;
        private FolderWidget _widget;
        private TaskbarController _taskbar;
        private PluginConfig _cfg;

        private bool _started;
        private bool _shutdown;
        private bool _wallpaperOn = true;
        private bool _fullscreen;
        private int _missingCount;

        private float _nextHeartbeat;
        private float _nextFastPoll;
        private bool _hotkeyWasDown;
        private int _fullscreenVk;
        private bool _fullscreenKeyWasDown;

        private System.Threading.Timer _diag;

        // ---------- 挂载 ----------

        public static Runner Create()
        {
            var go = new GameObject("ChillDesktop_Runner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            return go.AddComponent<Runner>();
        }

        private void Awake()
        {
            _instance = this;
            _cfg = ChillDesktopPlugin.Settings ?? new PluginConfig();

            _fullscreenVk = _cfg.FullscreenKey
                ? TaskbarController.ParseVirtualKey(_cfg.FullscreenToggleKey)
                : 0;
            if (_cfg.FullscreenKey && _fullscreenVk == 0)
                Log.Write($"全屏模式热键 \"{_cfg.FullscreenToggleKey}\" 无法识别，已禁用" +
                          "（支持 F1~F24 / A~Z / 0~9）。");

            // 屏幕物理像素：优先 Unity 的 Display（不受进程 DPI 感知方式影响）
            DesktopLayer.ScreenSizeProvider = () =>
                new Size(Display.main.systemWidth, Display.main.systemHeight);

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            Log.Write($"[Runner] 已挂载（scene={gameObject.scene.name}）" +
                      (_fullscreenVk != 0 ? $"，全屏模式热键={_cfg.FullscreenToggleKey}" : "，全屏模式热键=关闭"));
            StartCoroutine(Bootstrap());
        }

        private void OnProcessExit(object sender, EventArgs e) => Shutdown("ProcessExit");

        private void OnDestroy() => Shutdown("OnDestroy");

        private void OnApplicationQuit() => Shutdown("OnApplicationQuit");

        // ---------- 启动流程 ----------

        private IEnumerator Bootstrap()
        {
            // 必须用 WaitForSecondsRealtime：加载阶段 Time.timeScale 可能是 0，
            // 用 WaitForSeconds（缩放时间）协程会永远醒不过来。
            IntPtr hwnd = IntPtr.Zero;
            float deadline = Time.realtimeSinceStartup + 120f;
            int tries = 0;

            while (Time.realtimeSinceStartup < deadline)
            {
                hwnd = DesktopLayer.FindOwnMainWindow();
                if (hwnd != IntPtr.Zero) break;
                if (++tries % 10 == 1)
                    Log.Write($"尚未找到游戏主窗口（第 {tries} 次探测）");
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (hwnd == IntPtr.Zero)
            {
                Log.Write("等了 120 秒没等到游戏主窗口，插件放弃本次启动。");
                yield break;
            }

            int sysW = User32.GetSystemMetrics(User32.SM_CXSCREEN);
            int sysH = User32.GetSystemMetrics(User32.SM_CYSCREEN);
            Log.Write($"找到游戏主窗口 hwnd=0x{hwnd.ToInt64():X} 标题=\"{DesktopLayer.GetTitle(hwnd)}\"；" +
                      $"Unity 屏幕={Display.main.systemWidth}x{Display.main.systemHeight}，" +
                      $"GetSystemMetrics={sysW}x{sysH}");

            yield return new WaitForSecondsRealtime(Mathf.Max(0f, _cfg.SettleSeconds));

            try
            {
                StartWallpaper();
                _started = true;
            }
            catch (Exception ex)
            {
                Log.Error("Bootstrap", ex);
                SafeDetach();
                yield break;
            }

            float now = Time.realtimeSinceStartup;
            _nextHeartbeat = now + Mathf.Max(0.5f, _cfg.HeartbeatSeconds);
            _nextFastPoll = now + Mathf.Max(0.05f, _cfg.FastPollSeconds);
            Log.Write("ChillDesktop 壁纸模式已生效。");

            if (_cfg.HideTaskbarOnStart) SetFullscreen(true);

            if (_cfg.Diagnostics) StartDiagnostics();
        }

        private void StartWallpaper()
        {
            _layer = new DesktopLayer();
            _layer.EnsureLayers();

            _guard = new WindowGuard
            {
                BlockMinimize = _cfg.BlockMinimize,
                UseOwnerWindow = _cfg.UseOwnerWindow,
            };

            EmbedWallpaper();

            if (_guard.Attach(_layer.GameHwnd) && _guard.UseOwnerWindow)
                _guard.AttachOwnerWindow(_layer.GameHwnd);

            if (_cfg.HideDesktopIcons)
                _layer.HideIcons();

            if (_cfg.ShowToolbox)
            {
                _widget = new FolderWidget(_layer, new WidgetOptions
                {
                    RefW = _cfg.RefWidth,
                    RefH = _cfg.RefHeight,
                    NoteCx = _cfg.NoteCx,
                    NoteCy = _cfg.NoteCy,
                    NoteR = _cfg.NoteR,
                    ToolDx = _cfg.ToolDx,
                    ChillGirlPath = _cfg.ChillGirlPath,
                });
                _widget.ShowWidget();
            }
        }

        /// <summary>临时把壁纸收起来：还原游戏窗口、销毁工具箱、恢复任务栏，但保留守卫与心跳。</summary>
        private void HideWallpaper()
        {
            SetFullscreen(false);
            try { _widget?.Dispose(); } catch (Exception ex) { Log.Error("HideWallpaper.widget", ex); }
            _widget = null;
            try { _layer?.Detach(); } catch (Exception ex) { Log.Error("HideWallpaper.detach", ex); }
        }

        // ---------- 全屏模式（隐藏 Windows 任务栏） ----------

        private void SetFullscreen(bool on)
        {
            if (_fullscreen == on) return;

            // 任务栏控制器每次都新建：Explorer 重启后句柄会变，重建最省心
            if (_taskbar == null) _taskbar = new TaskbarController();

            if (on)
            {
                _fullscreen = _taskbar.Hide() || _fullscreen;
                if (_fullscreen) Log.Write("F11 全屏模式：已隐藏任务栏（再按一次恢复）");
            }
            else
            {
                _taskbar.Show();
                _fullscreen = false;
            }
        }

        // ---------- 每帧 ----------

        private void Update()
        {
            if (!_started || _shutdown) return;

            HandleHotkeys();

            float now = Time.realtimeSinceStartup;

            float fp = _cfg.FastPollSeconds;
            if (fp > 0f && now >= _nextFastPoll)
            {
                _nextFastPoll = now + Mathf.Max(0.05f, fp);
                FastPoll();
            }

            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + Mathf.Max(0.5f, _cfg.HeartbeatSeconds);
                SlowHeartbeat();
            }
        }

        private void HandleHotkeys()
        {
            // Ctrl+Alt+D：临时开关壁纸模式
            if (_cfg.ToggleHotkey)
            {
                bool down = User32.IsKeyDown(User32.VK_CONTROL) &&
                            User32.IsKeyDown(User32.VK_MENU) &&
                            User32.IsKeyDown(User32.VK_D);

                if (down && !_hotkeyWasDown)
                {
                    _hotkeyWasDown = true;
                    ToggleWallpaper();
                }
                else if (!down)
                {
                    _hotkeyWasDown = false;
                }
            }

            // F11（可配）：全屏模式，隐藏/恢复 Windows 任务栏
            if (_fullscreenVk != 0)
            {
                bool down = User32.IsKeyDown(_fullscreenVk);
                if (down && !_fullscreenKeyWasDown)
                {
                    _fullscreenKeyWasDown = true;
                    SetFullscreen(!_fullscreen);
                }
                else if (!down)
                {
                    _fullscreenKeyWasDown = false;
                }
            }
        }

        private void ToggleWallpaper()
        {
            _wallpaperOn = !_wallpaperOn;
            if (_wallpaperOn)
            {
                Log.Write("热键：恢复壁纸模式");
                try
                {
                    StartWallpaper();
                }
                catch (Exception ex) { Log.Error("ToggleWallpaper.on", ex); }
            }
            else
            {
                Log.Write("热键：临时关闭壁纸模式（还原游戏窗口）");
                HideWallpaper();
            }
        }

        /// <summary>200~300ms 级别的轻量检查：Win+D 把窗口最小化/藏起来时，0.25 秒内救回来。</summary>
        private void FastPoll()
        {
            try
            {
                if (!_wallpaperOn || _shutdown) return;

                IntPtr gh = _layer?.GameHwnd ?? IntPtr.Zero;
                if (gh == IntPtr.Zero) return;

                bool windowGone = !User32.IsWindow(gh);
                bool iconic = !windowGone && User32.IsIconic(gh);
                bool hidden = !windowGone && !User32.IsWindowVisible(gh);
                bool drifted = !windowGone && User32.GetParent(gh) != IntPtr.Zero;
                bool requested = _guard != null && _guard.RestoreRequested;
                if (requested && _guard != null) _guard.RestoreRequested = false;

                if (windowGone || iconic || hidden || drifted || requested)
                {
                    Log.Write($"[WinD] 快速自愈触发 gone={windowGone} iconic={iconic} " +
                              $"hidden={hidden} drifted={drifted} hookRequest={requested}");
                    RestoreWallpaper();
                }
            }
            catch (Exception ex) { Log.Error("FastPoll", ex); }
        }

        private void SlowHeartbeat()
        {
            try
            {
                if (_shutdown) return;

                // 全屏模式：Explorer 重启后任务栏句柄会换新，这里重新接管
                if (_fullscreen) _taskbar?.EnsureHidden();

                if (!_wallpaperOn) return;

                if (_layer == null || !_layer.Heartbeat())
                {
                    // 找不到窗口：游戏多半正在退出。桌面图标是我们藏起来的，
                    // 这里先抢在进程死掉之前还原掉（强杀场景来不及，另有应急脚本兜底）。
                    if (++_missingCount == 3)
                    {
                        Log.Write("连续 3 次找不到游戏窗口（游戏可能正在退出），先还原桌面图标。");
                        try { _layer?.RestoreIcons(); } catch (Exception ex) { Log.Error("RestoreIcons", ex); }
                    }
                    return;
                }
                _missingCount = 0;
                if (_cfg.HideDesktopIcons && _layer != null && !_layer.IconsHidden)
                    _layer.HideIcons();
                _widget?.Heartbeat();
            }
            catch (Exception ex) { Log.Error("SlowHeartbeat", ex); }
        }

        // ---------- 铺底 / 还原 ----------

        private void EmbedWallpaper()
        {
            _layer.EmbedGame();
            if (_guard != null && _guard.Attached && _guard.UseOwnerWindow)
                _guard.AttachOwnerWindow(_layer.GameHwnd);
        }

        /// <summary>被 Win+D 之类搞掉之后，把窗口拉回壁纸形态，并维持 工具箱→游戏 的沉底顺序。</summary>
        private void RestoreWallpaper()
        {
            try
            {
                IntPtr gh = _layer?.GameHwnd ?? IntPtr.Zero;
                if (gh != IntPtr.Zero && User32.IsWindow(gh))
                {
                    if (User32.IsIconic(gh))
                        User32.ShowWindow(gh, User32.SW_RESTORE);
                    if (!User32.IsWindowVisible(gh))
                        User32.ShowWindow(gh, User32.SW_SHOWNOACTIVATE);
                }

                _layer.EmbedGame();
                _widget?.Heartbeat(); // 顺带把工具箱重新沉到游戏正上方
            }
            catch (Exception ex) { Log.Error("RestoreWallpaper", ex); }
        }

        private void SafeDetach()
        {
            // 任务栏和桌面图标一样是「我们改过的系统状态」，退出必须还原
            try { _taskbar?.Show(); } catch (Exception ex) { Log.Error("SafeDetach.taskbar", ex); }
            _fullscreen = false;
            try { _widget?.Dispose(); } catch { }
            _widget = null;
            try { _guard?.Dispose(); } catch { }
            _guard = null;
            try { _layer?.Detach(); } catch { }
        }

        // ---------- 诊断 ----------

        private void StartDiagnostics()
        {
            _diag = new System.Threading.Timer(_ =>
            {
                try
                {
                    var h = DesktopLayer.FindOwnMainWindow();
                    Log.Write($"[诊断] 游戏窗口=0x{h.ToInt64():X} iconic={(h != IntPtr.Zero && User32.IsIconic(h))}");
                }
                catch (Exception ex) { Log.Error("diag", ex); }
            }, null, 5000, 5000);
        }

        // ---------- 退出清理 ----------

        private void Shutdown(string why)
        {
            if (_shutdown) return;
            _shutdown = true;

            try { _diag?.Dispose(); } catch { }
            _diag = null;

            if (_layer == null && _guard == null && _widget == null && _taskbar == null) return;

            Log.Write("插件卸载（" + why + "），还原游戏窗口、桌面图标与任务栏。");
            SafeDetach();
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
    }
}
