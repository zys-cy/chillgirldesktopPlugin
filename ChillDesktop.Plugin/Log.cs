using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;

namespace ChillDesktop
{
    /// <summary>
    /// 插件日志：同时写 BepInEx 控制台日志和 BepInEx\chilldesktop.log（后者便于事后取证）。
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static ManualLogSource _src;
        private static string _file;

        public static void Init(ManualLogSource src)
        {
            _src = src;
            try
            {
                _file = Path.Combine(Paths.BepInExRootPath, "chilldesktop.log");
            }
            catch { _file = null; }
        }

        public static void Write(string msg)
        {
            try { _src?.LogMessage(msg); } catch { }
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg;
            if (_file == null) return;
            try
            {
                lock (Gate)
                    File.AppendAllText(_file, line + Environment.NewLine);
            }
            catch { /* 日志失败不能影响主流程 */ }
        }

        public static void Error(string where, Exception ex)
            => Write($"[ERROR] {where}: {ex.GetType().Name} {ex.Message}");
    }
}
