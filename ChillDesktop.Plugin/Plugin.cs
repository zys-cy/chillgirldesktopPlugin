using System;
using System.Diagnostics;
using System.Drawing;
using BepInEx;
using ChillDesktop.Desktop;
using UnityEngine;

namespace ChillDesktop
{
    /// <summary>
    /// ChillDesktop —— 把《Chill with You Lo-Fi Story》变成可交互的动态桌面壁纸。
    ///
    /// 入口插件只负责“把主循环挂起来”。真正干活的 <see cref="Runner"/> 跑在插件自建的
    /// GameObject 上：本作在 BepInEx 给插件挂的那个对象创建后约 0.3 秒就把它销毁了
    /// （见 交接.md / README 的踩坑记录），挂在那个对象上的 Update 和协程全部不会执行。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class ChillDesktopPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.chilldesktop.plugin";
        public const string PluginName = "ChillDesktop";
        public const string PluginVersion = "0.1.0";

        internal const string ExpectedProcessName = "Chill With You";

        internal static PluginConfig Settings;

        private void Awake()
        {
            Log.Init(Logger);

            Log.Write($"ChillDesktop {PluginVersion} 载入，进程={SafeProcessName()}，" +
                      $"Unity={Application.unityVersion}，64位={IntPtr.Size == 8}");

            Settings = new PluginConfig();
            try
            {
                Settings.Bind(Config);
            }
            catch (Exception ex)
            {
                Log.Error("BindConfig", ex);
                return;
            }

            if (!Settings.Enabled)
            {
                Log.Write("cfg 里 Enabled=false，插件不工作。");
                return;
            }

            if (!Settings.ForceEnable &&
                SafeProcessName().IndexOf(ExpectedProcessName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                Log.Write($"当前进程不是《{ExpectedProcessName}》，插件自动停用" +
                          "（确实要强制启用就把 cfg 的 ForceEnable 改成 true）。");
                return;
            }

            Runner.Create();
        }

        /// <summary>
        /// 这个对象的销毁是必然的（游戏会干掉 BepInEx 挂插件的 GameObject），
        /// 所以这里什么都不做——清理逻辑在 Runner 的 OnDestroy / OnApplicationQuit 里。
        /// </summary>
        private void OnDestroy()
            => Log.Write("入口插件对象被销毁（预期行为，主循环跑在自建 GameObject 上）。");

        private static string SafeProcessName()
        {
            try { return Process.GetCurrentProcess().ProcessName; }
            catch { return "?"; }
        }
    }
}
