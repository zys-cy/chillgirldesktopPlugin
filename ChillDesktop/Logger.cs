using System;
using System.IO;

namespace ChillDesktop
{
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static readonly string FilePath =
            System.IO.Path.Combine(AppContext.BaseDirectory, "chilldesktop.log");

        public static void Write(string msg)
        {
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg;
            System.Diagnostics.Debug.WriteLine(line);
            try
            {
                lock (Gate)
                    File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch { /* 日志失败不能影响主流程 */ }
        }

        public static void Error(string where, Exception ex)
            => Write($"[ERROR] {where}: {ex.GetType().Name} {ex.Message}");
    }
}
